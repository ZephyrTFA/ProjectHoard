using System.Reflection;
using System.Runtime.Serialization;
using System.Text;

using Discord;
using Discord.Rest;
using Discord.WebSocket;

namespace Hoard2.Module
{
	struct ModuleFunctionMap
	{
		public ModuleFunctionMap() { }

		internal readonly List<CommandMap> SubCommands = new List<CommandMap>();
		internal bool IsPartial = false;

		internal void LoadFrom(List<Dictionary<string, string>> info)
		{
			IsPartial = true;
			SubCommands.Clear();
			foreach (var value in info)
			{
				var sCommand = new CommandMap();
				sCommand.LoadFrom(value);
				SubCommands.Add(sCommand);
			}
		}

		internal void SaveTo(List<Dictionary<string, string>> info)
		{
			foreach (var cMap in SubCommands)
			{
				var inf = new Dictionary<string, string>();
				cMap.SaveTo(inf);
				info.Add(inf);
			}
		}
	}

	struct CommandMap
	{
		internal string Name;
		internal string Description;
		internal MethodInfo Executor;
		internal List<ParameterInfo> Params;
		internal GuildPermission? Permission;

		List<Type> _loadedParamTypes;
		List<string> _loadedParamNames;
		List<object?> _loadedParamDefaults;

		internal List<string> ParamNames => (Params?.Select(param => param.Name!.MTrim()).ToList() ?? _loadedParamNames!)!;

		internal List<Type> ParamTypes => Params?.Select(param => param.ParameterType).ToList() ?? _loadedParamTypes;

		internal IEnumerable<object?> ParamDefaults
		{
			get
			{
				object? Convert(object? def)
				{
					if (def is DBNull)
						return null;
					return def;
				}

				if (Params is null) return _loadedParamDefaults;
				return Params.Select(param => param.DefaultValue).Select(Convert);
			}
		}

		internal bool NeedsRefresh(CommandMap other)
		{
			if (!Name.Equals(other.Name)) return true;
			if (!Description.Equals(other.Description)) return true;
			if (!Executor.Name.Equals(other.Executor.Name)) return true;
			if (!ParamTypes.SequenceEqual(other.ParamTypes)) return true;
			if (!ParamDefaults.SequenceEqual(other.ParamDefaults)) return true;
			if (!ParamNames.SequenceEqual(other.ParamNames)) return true;
			return false;
		}

		internal void LoadFrom(Dictionary<string, string> dict)
		{
			Name = dict[nameof(Name)];
			Description = dict[nameof(Description)];
			var executorDeclarer = dict["executor-decl"];
			var declarerType = Type.GetType(executorDeclarer);
			if (declarerType is null) throw new TypeUnloadedException();
			Executor = declarerType.GetMethod(dict[nameof(Executor)]) ?? throw new MissingMethodException();
			var paramTypes = dict["param-types"].Split("\0");
			var paramDefaults = dict["param-defaults"].Split("\0");
			var paramNames = dict["param-names"].Split("\0");
			_loadedParamDefaults = new List<object?>();
			_loadedParamTypes = new List<Type>();
			_loadedParamNames = new List<string>();
			foreach (var ((type, def), name) in paramTypes.Zip(paramDefaults).Zip(paramNames))
			{
				if (String.IsNullOrEmpty(type)) continue;
				var typeActual = Type.GetType(type) ?? throw new TypeUnloadedException();
				_loadedParamTypes.Add(typeActual);
				_loadedParamNames.Add(name);
				if (def == "-^|NULL|^-" || !typeActual.IsValueType)
					_loadedParamDefaults.Add(null);
				else
					_loadedParamDefaults.Add(typeActual.GetMethod("Parse", new[] { typeof(string) })!.Invoke(null, new object?[] { def }));
			}
		}

		internal void SaveTo(Dictionary<string, string> dict)
		{
			dict[nameof(Name)] = Name;
			dict[nameof(Description)] = Description;
			dict["executor-decl"] = Executor.DeclaringType!.AssemblyQualifiedName!;
			dict[nameof(Executor)] = Executor.Name;
			var (typeBuilder, defaultBuilder, nameBuilder) = (new StringBuilder(), new StringBuilder(), new StringBuilder());
			foreach (var ((pType, pDef), pName) in ParamTypes.Zip(ParamDefaults).Zip(ParamNames))
			{
				typeBuilder.Append($"{pType.AssemblyQualifiedName}\0");
				nameBuilder.Append($"{pName}\0");
				if (pDef is not null && pType == typeof(string) && !String.IsNullOrEmpty(pDef as string))
					defaultBuilder.Append($"{pDef.ToString()}");
				else
					defaultBuilder.Append("-^|NULL|^-");
				defaultBuilder.Append('\0');
			}
			dict["param-types"] = typeBuilder.ToString();
			dict["param-defaults"] = defaultBuilder.ToString();
			dict["param-names"] = nameBuilder.ToString();
		}
	}

	public static class CommandHelper
	{
		internal static readonly Dictionary<Type, ModuleFunctionMap> ModuleCommandMap = new Dictionary<Type, ModuleFunctionMap>();
		internal static readonly Dictionary<ulong, List<ModuleBase>> GuildCommandMap = new Dictionary<ulong, List<ModuleBase>>();
		internal static readonly Dictionary<string, ModuleBase> ModuleNameMap = new Dictionary<string, ModuleBase>();

		internal static FileInfo MapInfoStore = null!;

		public static async Task SaveMapInformation()
		{
			var data = new Dictionary<string, List<Dictionary<string, string>>>();
			foreach (var (type, mfMap) in ModuleCommandMap)
			{
				var mMap = new List<Dictionary<string, string>>();
				mfMap.SaveTo(mMap);
				data[type.AssemblyQualifiedName!] = mMap;
			}
			var serializer = new DataContractSerializer(typeof(Dictionary<string, List<Dictionary<string, string>>>));
			var memory = new MemoryStream();
			serializer.WriteObject(memory, data);
			memory.Seek(0, SeekOrigin.Begin);
			if (MapInfoStore.Exists) MapInfoStore.Delete();
			await using var fStream = MapInfoStore.Create();
			memory.WriteTo(fStream);
		}

		public static async Task LoadMapInformation()
		{
			if (!MapInfoStore.Exists) return;
			await using var fStream = MapInfoStore.OpenRead();
			var serializer = new DataContractSerializer(typeof(Dictionary<string, List<Dictionary<string, string>>>));
			if (serializer.ReadObject(fStream) is not Dictionary<string, List<Dictionary<string, string>>> data) throw new IOException();
			foreach (var (type, mfMap) in data)
			{
				var mMap = new ModuleFunctionMap();
				mMap.LoadFrom(mfMap);
				ModuleCommandMap[Type.GetType(type) ?? throw new TypeUnloadedException()] = mMap;
			}
		}

		public static async Task ProcessApplicationCommand(SocketSlashCommand command)
		{
			if (command.User is not SocketGuildUser user) return;
			if (!ModuleNameMap.TryGetValue(command.CommandName, out var commandOwner)) return;
			if (!ModuleHelper.IsModuleLoaded(command.GuildId!.Value, commandOwner))
				return;

			if (command.Data.Options.FirstOrDefault() is not { } commandData)
			{
				await commandOwner.ModuleCommand(command);
				return;
			}

			var subCommandName = commandData.Name;
			var moduleFunctionMap = ModuleCommandMap[commandOwner.GetType()];
			if (!moduleFunctionMap.SubCommands.Any(subCommand => subCommand.Name.Equals(subCommandName)))
				return;
			var subCommandMap = moduleFunctionMap.SubCommands.First(subCommand => subCommand.Name.Equals(subCommandName));

			if (subCommandMap.Permission is not null && !user.GuildPermissions.Has(subCommandMap.Permission.Value))
			{
				await command.RespondAsync("You do not have permission to run this command.", ephemeral: true);
				return;
			}

			var commandParams = new object?[subCommandMap.ParamTypes.Count + 1];
			var idx = 1;
			commandParams[0] = command;
			foreach (var (paramDefault, paramName) in subCommandMap.ParamDefaults.Zip(subCommandMap.ParamNames))
			{
				if (commandData.Options.FirstOrDefault(opt => opt.Name.Equals(paramName)) is { } option)
					commandParams[idx++] = option.Value;
				else
					commandParams[idx++] = paramDefault;
			}

			async Task RunCommand()
			{
				var executorResult = subCommandMap.Executor.Invoke(commandOwner, commandParams);
				if (executorResult is Task executorTask) await executorTask;
			}

			var runCommandTask = RunCommand();
			try
			{
				await runCommandTask.WaitAsync(TimeSpan.FromSeconds(5));
			}
			catch (TimeoutException)
			{
				await command.Channel.SendMessageAsync("Command cannot take longer than five seconds to return control to Gateway.");
			}
			catch (Exception e)
			{
				await command.Channel.SendMessageAsync($"Command threw an exception during invocation: `{e.Message}`");
			}
		}

		internal static string MTrim(this string str)
		{
			if (String.IsNullOrEmpty(str)) return str;
			var output = new StringBuilder();
			var first = str[0];
			output.Append(Char.ToLower(first));
			foreach (var character in str.Skip(1))
				if (Char.IsUpper(character))
					output.Append($"-{Char.ToLower(character)}");
				else
					output.Append(character);
			return output.ToString();
		}

		public static async Task UpdateGuildModuleCommand(ulong guildId, ModuleBase module)
		{
			var refreshOptional = await RefreshModuleCommands(guildId, module);
			if (!refreshOptional.IsSpecified) return;
			var builder = refreshOptional.Value;
			var guild = HoardMain.DiscordClient.GetGuild(guildId);
			var existing = (await guild.GetApplicationCommandsAsync()).FirstOrDefault(command => command.Name.Equals(builder.Name));
			if (existing is { })
				await existing.DeleteAsync();
			await guild.CreateApplicationCommandAsync(builder.Build());
		}

		public static async Task WipeGuildModuleCommand(ulong guildId, ModuleBase module)
		{
			var guild = HoardMain.DiscordClient.GetGuild(guildId);
			var existing = (await guild.GetApplicationCommandsAsync()).FirstOrDefault(command => command.Name.Equals(module.GetModuleName()));
			if (existing is { })
				await existing.DeleteAsync();
			GuildCommandMap.Remove(guildId);
			ModuleNameMap.Remove(module.GetModuleName());
			ModuleCommandMap.Remove(module.GetType());
			await SaveMapInformation();
		}

		public static async Task WipeAllGuildCommands(SocketSlashCommand? originator = null)
		{
			var allCommands = HoardMain.DiscordClient.Guilds.SelectMany(discordClientGuild =>
				discordClientGuild.GetApplicationCommandsAsync().GetAwaiter().GetResult()).ToList();

			for (var i = 0; i < allCommands.Count; i++)
			{
				if (i % 5 == 0)
					await (originator?.ModifyOriginalResponse($"Wiping {i}/{allCommands.Count}") ?? Task.CompletedTask);
				await allCommands[i].DeleteAsync();
			}
			await (originator?.ModifyOriginalResponse("Complete") ?? Task.CompletedTask);

			GuildCommandMap.Clear();
			ModuleNameMap.Clear();
			ModuleCommandMap.Clear();
			await SaveMapInformation();
		}

		public static async Task RefreshAllGuildCommands()
		{
			foreach (var discordClientGuild in HoardMain.DiscordClient.Guilds)
			{
				if (!ModuleHelper.GuildModules.ContainsKey(discordClientGuild.Id)) continue;
				foreach (var loadedModule in ModuleHelper.GuildModules[discordClientGuild.Id])
					await UpdateGuildModuleCommand(discordClientGuild.Id, ModuleHelper.ModuleInstances[loadedModule]);
			}
		}

		public static async Task<Optional<SlashCommandBuilder>> RefreshModuleCommands(ulong guild, ModuleBase module)
		{
			HoardMain.Logger.LogInformation("Refreshing module commands for {}", module);
			var moduleMap = new ModuleFunctionMap();
			var possibleFunctions = module.GetType().GetMethods();
			var filteredFunctions = possibleFunctions.Where(func => func.GetCustomAttribute<ModuleCommandAttribute>() is { }).Select(func => (func.GetCustomAttribute<ModuleCommandAttribute>()!, func)).ToList();
			ModuleFunctionMap? knownCommand = ModuleCommandMap.TryGetValue(module.GetType(), out var knownMap) ? knownMap : null;
			var knownSubCommands = knownCommand?.SubCommands ?? new List<CommandMap>();
			var refreshed = new List<CommandMap>();
			var refreshNeeded = !knownCommand.HasValue;
			foreach (var (moduleCommandAttribute, executor) in filteredFunctions)
			{
				var functionMap = new CommandMap
				{
					Name = (moduleCommandAttribute.CommandName ?? executor.Name).MTrim(),
					Description = moduleCommandAttribute.CommandDescription,
					Executor = executor,
					Params = executor.GetParameters().Skip(1).ToList(), // could technically just get this from Executor, but this is nicer
					Permission = moduleCommandAttribute.CommandPermissionRequirements,
				};

				bool FindPred(CommandMap commMap) => commMap.Executor.Name.Equals(executor.Name);
				if (knownSubCommands.Any(FindPred))
				{
					var known = knownSubCommands.First(FindPred);
					if (!known.NeedsRefresh(functionMap))
					{
						known.Params = functionMap.Params;
						known.Permission = functionMap.Permission;
						refreshed.Add(known);
						continue;
					}
				}

				refreshNeeded = true;
				refreshed.Add(functionMap);
			}

			if (!GuildCommandMap.ContainsKey(guild))
				GuildCommandMap[guild] = new List<ModuleBase>();
			if (!GuildCommandMap[guild].Contains(module))
				GuildCommandMap[guild].Add(module);
			ModuleNameMap[module.GetModuleName()] = module;

			if (!refreshNeeded)
			{
				HoardMain.Logger.LogInformation("Refresh is not needed.");
				return Optional<SlashCommandBuilder>.Unspecified;
			}

			var commandBuilder = new SlashCommandBuilder { Name = module.GetModuleName(), Description = "Module command" };
			foreach (var commandMap in refreshed)
			{
				moduleMap.SubCommands.Add(commandMap);
				var functionOption = new SlashCommandOptionBuilder { Name = commandMap.Name, Description = commandMap.Description, Type = ApplicationCommandOptionType.SubCommand };
				foreach (var param in commandMap.Params)
					functionOption.AddOption(new SlashCommandOptionBuilder
					{
						Name = param.Name!.MTrim(),
						Description = param.Name!,
						Type = param.ParameterType.AsOptionType(),
						IsRequired = param.DefaultValue is null or DBNull,
					});
				commandBuilder.AddOption(functionOption);
			}

			ModuleCommandMap[module.GetType()] = moduleMap;
			await SaveMapInformation();
			HoardMain.Logger.LogInformation("Refreshing complete. Update required.");
			return new Optional<SlashCommandBuilder>(commandBuilder);
		}

		public static async Task RunLongCommandTask(Func<Task> action, RestInteractionMessage responseMessage)
		{
			try
			{
				var task = action.Invoke();
				await task;
				if (task.Exception is { }) throw task.Exception;
			}
			catch (Exception exception)
			{
				await responseMessage.Channel.SendMessageAsync($"Command experienced an Exception: \n```\n{exception.Message[..Math.Min(exception.Message.Length, 1500)]}\n```\n");
			}
		}

		public static async Task ModifyOriginalResponse(this SocketSlashCommand response, string content)
		{
			var responseMessage = await response.GetOriginalResponseAsync();
			await responseMessage.ModifyAsync(props => props.Content = content);
		}

		public static ApplicationCommandOptionType AsOptionType(this Type type)
		{
			if (type == typeof(bool))
				return ApplicationCommandOptionType.Boolean;

			if (type == typeof(IChannel))
				return ApplicationCommandOptionType.Channel;

			// ints are not supported by discord, use long!
			// if (type == typeof(int))
			// 	return ApplicationCommandOptionType.Integer;

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

			throw new ArgumentOutOfRangeException(nameof(type), $"Unknown argument type '{type.Name}'");
		}
	}
}
