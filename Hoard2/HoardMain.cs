using System.Reflection;
using System.Runtime.Serialization;

using Discord;
using Discord.WebSocket;

using Hoard2.Module;

namespace Hoard2
{
	public static class HoardMain
	{
		public static IHost HoardHost = null!;
		public static ILogger<Worker> Logger = null!;
		public static DiscordSocketClient DiscordClient = null!;
		public static DirectoryInfo DataDirectory = null!;

		public static readonly Dictionary<ulong, Dictionary<string, ModuleBase>> ModuleCache = new Dictionary<ulong, Dictionary<string, ModuleBase>>();
		public static readonly Dictionary<ulong, KeyValuePair<SocketApplicationCommand, MethodInfo>> ApplicationCommands = new Dictionary<ulong, KeyValuePair<SocketApplicationCommand, MethodInfo>>();
		public static readonly Dictionary<ulong, Dictionary<string, List<SocketApplicationCommand>>> ModuleCommands = new Dictionary<ulong, Dictionary<string, List<SocketApplicationCommand>>>();

		public static void StopWorker(int exitCode = 0)
		{
			Environment.ExitCode = exitCode;
			HoardHost.StopAsync(CancellationToken.None);
		}

		public static void Initialize(ILogger<Worker> log, CancellationToken workerToken)
		{
			DiscordClient = new DiscordSocketClient(new DiscordSocketConfig
			{
				GatewayIntents = GatewayIntents.All,
			});
			Logger = log;
			DiscordClient.Log += HandleDiscordLog;
			DataDirectory = new DirectoryInfo($"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}/ProjectHoard");
			if (!DataDirectory.Exists)
				DataDirectory.Create();
		}

		static Task HandleDiscordLog(LogMessage message)
		{
			var level = message.Severity switch
			{
				LogSeverity.Critical => LogLevel.Critical,
				LogSeverity.Debug => LogLevel.Debug,
				LogSeverity.Error => LogLevel.Error,
				LogSeverity.Info => LogLevel.Information,
				LogSeverity.Verbose => LogLevel.Trace,
				LogSeverity.Warning => LogLevel.Warning,
				_ => LogLevel.None,
			};

			Logger.Log(level, message.Exception, "Discord: {}", message.Message);
			return Task.CompletedTask;
		}

		public static async Task<bool> Start()
		{
			LinkEvents();

			var ready = false;
			Task OnReady() => Task.FromResult(ready = true);

			string token;
			var tokenFile = Path.Join(DataDirectory.FullName, "hoard.token");
			if (!File.Exists(tokenFile))
			{
				if (Environment.GetEnvironmentVariable("PROJECT_HOARD_TOKEN", EnvironmentVariableTarget.Process) is { } envToken)
				{
					token = envToken;
				}
				else
				{
					Logger.LogCritical("Must populate '{TokenFile}' on disk or PROJECT_HOARD_TOKEN as an env var", tokenFile);
					return false;
				}
			}
			else
			{
				token = await File.ReadAllTextAsync(tokenFile);
			}

			DiscordClient.Ready += OnReady;
			await DiscordClient.LoginAsync(TokenType.Bot, token);
			await DiscordClient.StartAsync();

			var checksLeft = 5;
			while (--checksLeft > 0 && !ready)
				await Task.Delay(1000);
			DiscordClient.Ready -= OnReady;

			if (!ready)
			{
				Logger.LogCritical("Failed to establish connection to Discord!");
				await DiscordClient.StopAsync();
				await DiscordClient.DisposeAsync();
				return false;
			}

			await DiscordClient.SetStatusAsync(UserStatus.DoNotDisturb);
			await DiscordClient.CurrentUser.ModifyAsync(properties => properties.Username = "Project Hoard");

			await RestoreModules();

			await DiscordClient.SetStatusAsync(UserStatus.Online);
			Logger.LogInformation("Hoard Ready");
			return true;
		}

		public static async Task Shutdown()
		{
			await DiscordClient.SetStatusAsync(UserStatus.Offline);
			await DiscordClient.StopAsync();
		}

		public static void LinkEvents()
		{
			DiscordClient.MessageReceived += DiscordClientOnMessageReceived;
			DiscordClient.MessageDeleted += DiscordClientOnMessageDeleted;
			DiscordClient.MessageUpdated += DiscordClientOnMessageUpdated;
			DiscordClient.UserJoined += DiscordClientOnUserJoined;
			DiscordClient.UserLeft += DiscordClientOnUserLeft;
			DiscordClient.SlashCommandExecuted += DiscordClientOnSlashCommandExecuted;
		}

		public static bool UnloadModule(ulong guild, string moduleId, out string reason)
		{
			if (!ModuleCache[guild].ContainsKey(moduleId))
			{
				reason = "Module not loaded";
				return false;
			}

			if (ModuleCommands.ContainsKey(guild) && ModuleCommands[guild].ContainsKey(moduleId))
			{
				foreach (var command in ModuleCommands[guild][moduleId])
				{
					ApplicationCommands.Remove(command.Id);
					command.DeleteAsync().Wait();
				}
				ModuleCommands[guild].Remove(moduleId);
			}

			ModuleCache[guild].Remove(moduleId);
			reason = "Unloaded successfully";
			UpdateLoadedModules(guild);
			return true;
		}

		public static bool LoadModule(ulong guild, string moduleId, out string reason)
		{
			moduleId = moduleId.ToLower();
			if (!ModuleHelper.Modules.TryGetValue(moduleId, out var moduleType))
			{
				reason = "Module not found";
				return false;
			}

			if (!ModuleCache.ContainsKey(guild)) ModuleCache[guild] = new Dictionary<string, ModuleBase>();
			if (ModuleCache[guild].ContainsKey(moduleId))
			{
				reason = "Module already loaded";
				return false;
			}

			var moduleConstructor = moduleType.GetConstructor(new[] { typeof(ulong), typeof(string) });
			if (moduleConstructor is null)
			{
				reason = "Module not valid";
				return false;
			}

			try
			{
				if (!AssembleSlashCommands(guild, moduleId, moduleType, out reason))
					return false;
			}
			catch (Exception exception)
			{
				reason = $"Exception during command assembly: {exception}";
				return false;
			}

			var configDir = GetGuildConfigFolder(guild);
			var configPath = Path.Join(configDir, $"{moduleId}.xml");
			var moduleInstance = (ModuleBase)moduleConstructor.Invoke(new object[] { guild, configPath });
			ModuleCache[guild][moduleId] = moduleInstance;
			reason = "Loaded successfully";
			UpdateLoadedModules(guild);
			return true;
		}

		static void UpdateLoadedModules(ulong guild)
		{
			var loaded = ModuleCache[guild].Keys.ToArray();
			var store = Path.Join(GetGuildConfigFolder(guild), "loaded.xml");
			if (File.Exists(store)) File.Delete(store);
			using var writer = File.OpenWrite(store);
			new DataContractSerializer(typeof(string[])).WriteObject(writer, loaded);
			writer.Dispose();
		}

		static IEnumerable<string> CheckLoadedModules(ulong guild)
		{
			var store = Path.Join(GetGuildConfigFolder(guild), "loaded.xml");
			if (!File.Exists(store))
				return Array.Empty<string>();
			using var reader = File.OpenRead(store);
			return (string[]?)new DataContractSerializer(typeof(string[])).ReadObject(reader) ?? Array.Empty<string>();
		}

		static bool AssembleSlashCommands(ulong guild, string moduleId, Type moduleType, out string reason)
		{
			var moduleCommands = moduleType.GetMethods().Where(info => info.GetCustomAttribute<ModuleCommandAttribute>() is { })
				.Select(info => new KeyValuePair<MethodInfo, ModuleCommandAttribute>(info, info.GetCustomAttribute<ModuleCommandAttribute>()!));

			foreach (var (command, data) in moduleCommands)
			{
				var slashCommandBuilder = new SlashCommandBuilder
				{
					Name = data.CommandName,
					DefaultMemberPermissions = data.CommandPermissionRequirements,
					Description = data.CommandDescription,
				};

				var paramCount = data.CommandParamNames.Length;
				if (data.CommandParamTypes.Length != paramCount || data.CommandParamDescriptions.Length != paramCount)
				{
					reason = "Invalid parameter information: mismatched array sizes";
					return false;
				}

				for (var param = 0; param < paramCount; param++)
				{
					var optionType = data.CommandParamTypes[param].AsOptionType();
					if (!optionType.HasValue)
					{
						reason = $"Not a known option type: {data.CommandParamTypes[param]}";
						return false;
					}

					var option = new SlashCommandOptionBuilder
					{
						Name = data.CommandParamNames[param],
						Type = optionType.Value,
						Description = data.CommandParamDescriptions[param],
					};

					slashCommandBuilder.AddOption(option);
				}

				var slashCommand = DiscordClient.CreateGlobalApplicationCommandAsync(slashCommandBuilder.Build(), new RequestOptions { RetryMode = RetryMode.AlwaysRetry }).GetAwaiter().GetResult();
				ApplicationCommands[slashCommand.Id] = new KeyValuePair<SocketApplicationCommand, MethodInfo>(slashCommand, command);
				if (!ModuleCommands.ContainsKey(guild)) ModuleCommands[guild] = new Dictionary<string, List<SocketApplicationCommand>>();
				if (!ModuleCommands[guild].ContainsKey(moduleId)) ModuleCommands[guild][moduleId] = new List<SocketApplicationCommand>();
				ModuleCommands[guild][moduleId].Add(slashCommand);
			}

			reason = String.Empty;
			return true;
		}

		public static string GetGuildConfigFolder(ulong guild) => DataDirectory.CreateSubdirectory(guild.ToString()).FullName;

		static ApplicationCommandOptionType? AsOptionType(this Type type)
		{
			if (type == typeof(bool))
				return ApplicationCommandOptionType.Boolean;
			if (type == typeof(IChannel))
				return ApplicationCommandOptionType.Channel;
			if (type == typeof(long))
				return ApplicationCommandOptionType.Integer;
			if (type == typeof(double))
				return ApplicationCommandOptionType.Number;
			if (type == typeof(IRole))
				return ApplicationCommandOptionType.Role;
			if (type == typeof(string))
				return ApplicationCommandOptionType.String;
			if (type == typeof(IUser))
				return ApplicationCommandOptionType.User;
			return null;
		}

		public static async Task RestoreModules()
		{
			foreach (var command in await DiscordClient.GetGlobalApplicationCommandsAsync())
				await command.DeleteAsync(new RequestOptions { RetryMode = RetryMode.AlwaysFail });

			ModuleHelper.Modules.Clear();
			ModuleHelper.LoadAssembly(Assembly.GetExecutingAssembly(), out _);

			foreach (var guild in DiscordClient.Guilds.Select(guild => guild.Id))
				foreach (var module in CheckLoadedModules(guild))
					LoadModule(guild, module, out _);
		}

		static async Task DiscordClientOnSlashCommandExecuted(SocketSlashCommand arg)
		{
			if (!arg.GuildId.HasValue)
				return;
			if (!ApplicationCommands.TryGetValue(arg.CommandId, out var handler))
				return;
			var commandHandler = handler.Value;
			var commandClass = commandHandler.DeclaringType!.Name.ToLower();
			try
			{
				await (Task)commandHandler.Invoke(ModuleCache[arg.GuildId.Value][commandClass], new object?[] { arg })!;
			}
			catch (Exception e)
			{
				await arg.RespondAsync($"Failed to process command: {e.Message}", ephemeral: true);
			}
		}

		static List<ModuleBase> GetModules(this ulong guild)
		{
			if (!ModuleCache.TryGetValue(guild, out var modules))
				return new List<ModuleBase>();
			return modules.Values.ToList();
		}

		static async Task DiscordClientOnUserLeft(SocketGuild arg1, SocketUser arg2)
		{
			foreach (var module in arg1.Id.GetModules())
				await module.DiscordClientOnUserLeft(arg2);
		}

		static async Task DiscordClientOnUserJoined(SocketGuildUser arg)
		{
			foreach (var module in arg.Guild.Id.GetModules())
				await module.DiscordClientOnUserJoined(arg);
		}

		static async Task DiscordClientOnMessageUpdated(Cacheable<IMessage, ulong> arg1, SocketMessage arg2, ISocketMessageChannel arg3)
		{
			if (arg3 is not IGuildChannel guildChannel)
				return;
			foreach (var module in guildChannel.GuildId.GetModules())
				await module.DiscordClientOnMessageUpdated(arg1, arg2, arg3);
		}

		static async Task DiscordClientOnMessageDeleted(Cacheable<IMessage, ulong> arg1, Cacheable<IMessageChannel, ulong> arg2)
		{
			if (!arg2.HasValue || arg2.Value is not IGuildChannel guildChannel)
				return;
			foreach (var module in guildChannel.GuildId.GetModules())
				await module.DiscordClientOnMessageDeleted(arg1, arg2);
		}

		static async Task<bool> HandleSystemCommand(IMessage arg)
		{
			if (arg.Author.Id != 946283057915232337)
				return false;

			if (arg.Content.StartsWith("?load_hmd"))
			{
				if (!LoadModule((arg.Channel as IGuildChannel)!.GuildId, arg.Content.Replace("?load_hmd", "").ToLower().Trim(), out var reason))
					await arg.Channel.SendMessageAsync($"Failed to load module: {reason}");
				else
					await arg.Channel.SendMessageAsync("Loaded");
				return true;
			}

			if (arg.Content.StartsWith("?unload_hmd"))
			{
				if (!UnloadModule(((IGuildChannel)arg.Channel).GuildId, arg.Content.Replace("?unload_hmd", "").ToLower().Trim(), out var reason))
					await arg.Channel.SendMessageAsync($"Failed to unload module: {reason}");
				else
					await arg.Channel.SendMessageAsync("Unloaded");
				return true;
			}

			switch (arg.Content)
			{
				case "?hoard_restart":
					await arg.Channel.SendMessageAsync("Restarting...");
					Logger.LogInformation("Restarting...");
					StopWorker();
					return true;

				case "?hoard_shutdown":
					await arg.Channel.SendMessageAsync("Shutting down...");
					Logger.LogInformation("Shutting down gracefully...");
					StopWorker(69);
					return true;

				default:
					return false;
			}

		}

		static async Task DiscordClientOnMessageReceived(IMessage arg)
		{
			// why is this needed?
			await Task.Yield();
			arg = await arg.Channel.GetMessageAsync(arg.Id);

			if (arg.Channel is not IGuildChannel guildChannel)
				return;
			if (await HandleSystemCommand(arg))
				return;
			foreach (var module in guildChannel.GuildId.GetModules())
				await module.DiscordClientOnMessageReceived(arg);
		}
	}
}
