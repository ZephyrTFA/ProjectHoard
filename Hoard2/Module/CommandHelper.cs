using System.Reflection;
using System.Text;

using Discord;
using Discord.Rest;
using Discord.WebSocket;

namespace Hoard2.Module
{
	struct ModuleFunctionMap
	{
		public ModuleFunctionMap(ModuleBase owner)
		{
			Owner = owner;
		}

		internal readonly ModuleBase Owner;
		internal readonly Dictionary<string, string[]> ParamMap = new Dictionary<string, string[]>();
		internal readonly Dictionary<string, Dictionary<string, object?>> ParamDefaultMap = new Dictionary<string, Dictionary<string, object?>>();
		internal readonly Dictionary<string, MethodInfo> ExecutorMap = new Dictionary<string, MethodInfo>();
		internal readonly Dictionary<string, GuildPermission> PermissionMap = new Dictionary<string, GuildPermission>();
	}

	public static class CommandHelper
	{
		static readonly Dictionary<string, ModuleFunctionMap> ModuleFunctionMap = new Dictionary<string, ModuleFunctionMap>();

		public static async Task ProcessApplicationCommand(SocketSlashCommand command)
		{
			if (command.GuildId is null)
				return;

			var commandName = command.CommandName.ToLower().Trim();
			if (!ModuleFunctionMap.ContainsKey(commandName))
				return;
			var map = ModuleFunctionMap[commandName];
			var commandData = command.Data;

			var subCommand = commandData.Options.FirstOrDefault();
			if (subCommand is null)
			{
				await command.RespondAsync("No command given.");
				return;
			}
			var subCommandName = subCommand.Name;

			if (map.PermissionMap.TryGetValue(subCommandName, out var permission))
			{
				var user = (SocketGuildUser)command.User;
				if (!user.GuildPermissions.Has(permission))
				{
					await command.RespondAsync("You lack permission for this command!", ephemeral: true);
					return;
				}
			}

			var options = subCommand.Options;
			var paramsGiven = options.Select(opt => opt.Name).ToList();
			var methodParams = new object?[map.ParamMap[subCommandName].Length + 1];
			var idx = 1;
			foreach (var param in map.ParamMap[subCommandName])
			{
				if (paramsGiven.Contains(param))
					methodParams[idx++] = options.FirstOrDefault(opt => opt.Name.Equals(param))?.Value;
				else methodParams[idx++] = map.ParamDefaultMap[subCommandName][param];
			}
			methodParams[0] = command;

			var executor = map.ExecutorMap[subCommandName];
			var executorTask = (Task)executor.Invoke(map.Owner, methodParams)!;

			try
			{
				await executorTask.WaitAsync(TimeSpan.FromSeconds(5));
			}
			catch (TimeoutException)
			{
				await command.Channel.SendMessageAsync("Command is causing a timeout. A slash command cannot take longer than five seconds to return control to the Gateway.");
			}
			catch (Exception e)
			{
				await command.Channel.SendMessageAsync($"Command experienced an Exception:\n```\n{e.Message[.. Math.Min(e.Message.Length, 1500)]}\n```\n");
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

		public static bool RefreshModuleCommands(ulong guild, ModuleBase module, out string reason)
		{
			HoardMain.Logger.LogInformation("Refreshing module commands for {}", module);

			var moduleCommands = module.GetType().GetMethods().Where(info => info.GetCustomAttribute<ModuleCommandAttribute>() is { })
				.Select(info => new KeyValuePair<MethodInfo, ModuleCommandAttribute>(info, info.GetCustomAttribute<ModuleCommandAttribute>()!)).ToList();
			if (moduleCommands.Count == 0)
			{
				reason = "nothing to load";
				return true;
			}

			var moduleMasterCommandName = module.GetType().Name.MTrim();
			var moduleMasterCommand = new SlashCommandBuilder
			{
				Name = moduleMasterCommandName,
				Description = $"Commands for {moduleMasterCommandName}",
			};

			var moduleFunctionMap = new ModuleFunctionMap(module);
			foreach (var (methodInfo, commandAttribute) in moduleCommands)
			{
				if (methodInfo.ReturnType != typeof(Task))
				{
					reason = "application command method must return Task";
					return false;
				}

				var methodParams = methodInfo.GetParameters();
				if (methodParams[0].ParameterType != typeof(SocketSlashCommand))
				{
					reason = "first parameter of an application command must be SocketSlashCommand";
					return false;
				}
				methodParams = methodParams.Skip(1).ToArray();

				var moduleName = commandAttribute.CommandName ?? methodInfo.Name;
				var moduleSubCommand = new SlashCommandOptionBuilder
				{
					Name = moduleName.MTrim(),
					Description = commandAttribute.CommandDescription,
					Type = ApplicationCommandOptionType.SubCommand,
				};
				if (commandAttribute.CommandPermissionRequirements.HasValue)
					moduleFunctionMap.PermissionMap[moduleSubCommand.Name] = commandAttribute.CommandPermissionRequirements.Value;

				moduleFunctionMap.ParamMap[moduleSubCommand.Name] = new string[methodParams.Length];
				moduleFunctionMap.ParamDefaultMap[moduleSubCommand.Name] = new Dictionary<string, object?>();
				var paramIdx = 0;
				foreach (var param in methodParams)
				{
					var parsedType = param.ParameterType.AsOptionType();
					if (!parsedType.HasValue)
					{
						reason = $"unable to parse param type {param.ParameterType}";
						return false;
					}

					var paramName = param.Name!.MTrim();

					var (required, type) = parsedType.Value;
					if (param.HasDefaultValue)
					{
						required = false;
						moduleFunctionMap.ParamDefaultMap[moduleSubCommand.Name][paramName] = param.DefaultValue;
					}
					var moduleCommandParam = new SlashCommandOptionBuilder
					{
						Name = paramName,
						Description = param.Name!.MTrim(),
						IsRequired = required,
						Type = type,
					};
					moduleFunctionMap.ParamMap[moduleSubCommand.Name][paramIdx++] = moduleCommandParam.Name;
					moduleSubCommand.AddOption(moduleCommandParam);
				}

				moduleMasterCommand.AddOption(moduleSubCommand);
				moduleFunctionMap.ExecutorMap[moduleSubCommand.Name] = methodInfo;
			}

			try
			{
				var guildInstance = HoardMain.DiscordClient.GetGuild(guild);
				guildInstance.CreateApplicationCommandAsync(moduleMasterCommand.Build()).Wait();
			}
			catch (Exception e)
			{
				reason = $"exception during creation: {e}";
				return false;
			}

			ModuleFunctionMap[moduleMasterCommandName] = moduleFunctionMap;
			reason = String.Empty;
			return true;
		}

		public static void ClearModuleCommand(ulong guild, ModuleBase module)
		{
			var guildInstance = HoardMain.DiscordClient.GetGuild(guild);
			var guildCommands = guildInstance.GetApplicationCommandsAsync().GetAwaiter().GetResult();
			guildCommands.FirstOrDefault(command => command.Name.MTrim().Equals(module.GetType().Name.MTrim()))?.DeleteAsync().Wait();
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

		static(bool, ApplicationCommandOptionType)? AsOptionType(this Type type)
		{
			var required = true;
			if (Nullable.GetUnderlyingType(type) is { } nullableType)
			{
				required = false;
				type = nullableType;
			}

			if (type == typeof(bool))
				return (required, ApplicationCommandOptionType.Boolean);

			if (type == typeof(IChannel))
				return (required, ApplicationCommandOptionType.Channel);

			// ints are not supported by discord, use long!
			// if (type == typeof(int))
			// 	return (required, ApplicationCommandOptionType.Integer);

			if (type == typeof(long))
				return (required, ApplicationCommandOptionType.Integer);

			if (type == typeof(double))
				return (required, ApplicationCommandOptionType.Number);

			if (type == typeof(IRole))
				return (required, ApplicationCommandOptionType.Role);

			if (type == typeof(string))
				return (required, ApplicationCommandOptionType.String);

			if (type == typeof(IUser))
				return (required, ApplicationCommandOptionType.User);

			return null;
		}
	}
}
