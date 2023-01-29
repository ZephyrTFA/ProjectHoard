using System.Reflection;

using Discord;
using Discord.WebSocket;

namespace Hoard2.Module
{
	public static class CommandHelper
	{
		public static readonly Dictionary<string, Dictionary<ulong, SocketApplicationCommand>> GuildCommands = new Dictionary<string, Dictionary<ulong, SocketApplicationCommand>>();
		public static readonly Dictionary<string, ModuleBase> CommandOwner = new Dictionary<string, ModuleBase>();
		public static readonly Dictionary<string, MethodInfo> CommandExecutor = new Dictionary<string, MethodInfo>();

		public static async Task ProcessApplicationCommand(SocketSlashCommand command)
		{
			if (!CommandExecutor.TryGetValue(command.CommandName, out var executor))
				return;
			var executorTask = (Task)executor.Invoke(CommandOwner[command.CommandName], new object?[] { command })!;
			try
			{
				await executorTask.WaitAsync(TimeSpan.FromSeconds(5));
			}
			catch (TimeoutException)
			{
				const string message = "Command is causing a timeout. A slash command cannot take longer than five seconds to return control to the Gateway.";
				await command.Channel.SendMessageAsync(message);
			}
		}

		public static bool RefreshModuleCommands(ulong guild, ModuleBase module, out string reason)
		{
			HoardMain.Logger.LogInformation("Refreshing module commands for {}", module);
			var moduleCommands = module.GetType().GetMethods().Where(info => info.GetCustomAttribute<ModuleCommandAttribute>() is { })
				.Select(info => new KeyValuePair<MethodInfo, ModuleCommandAttribute>(info, info.GetCustomAttribute<ModuleCommandAttribute>()!));

			foreach (var (command, data) in moduleCommands)
			{
				if (command.ReturnType != typeof(Task))
				{
					reason = "command function does not return Task";
					return false;
				}
				var parameters = command.GetParameters();
				if (parameters.Length != 1 || parameters[0].ParameterType != typeof(SocketSlashCommand))
				{
					reason = "command function expects one paramter of SocketSlashCommand";
					return false;
				}

				var commandName = data.CommandName.ToLower().Trim();
				var slashCommandBuilder = new SlashCommandBuilder
				{
					Name = commandName,
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
					var optTypeInfo = data.CommandParamTypes[param].AsOptionType();
					if (!optTypeInfo.HasValue)
					{
						reason = $"Not a known option type: {data.CommandParamTypes[param]}";
						return false;
					}
					var (required, optionType) = optTypeInfo.Value;

					var option = new SlashCommandOptionBuilder
					{
						Name = data.CommandParamNames[param],
						Type = optionType,
						Description = data.CommandParamDescriptions[param],
						IsRequired = required,
					};

					slashCommandBuilder.AddOption(option);
				}

				var commandProps = slashCommandBuilder.Build();
				if (!GuildCommands.ContainsKey(commandName)) GuildCommands[commandName] = new Dictionary<ulong, SocketApplicationCommand>();
				if (GuildCommands[commandName].TryGetValue(guild, out var existingCommand))
				{
					existingCommand.DeleteAsync().Wait();
					HoardMain.Logger.LogInformation("Removing guild old command");
				}
				HoardMain.Logger.LogInformation("Creating command {}", commandName);

				var guildInstance = HoardMain.DiscordClient.GetGuild(guild);
				var commandInstance = guildInstance.CreateApplicationCommandAsync(commandProps,
					new RequestOptions { RetryMode = RetryMode.RetryRatelimit }).GetAwaiter().GetResult();
				GuildCommands[commandName].Add(guild, commandInstance);

				// These will get reset everytime a guild is loaded, oh well!
				CommandExecutor[commandName] = command;
				CommandOwner[commandName] = module;
			}

			reason = String.Empty;
			return true;
		}

		public static void ClearModuleCommands(ulong guild, ModuleBase module)
		{
			foreach (var command in CommandOwner.Where(kvp => kvp.Value == module).Select(kvp => kvp.Key))
			{
				if (!GuildCommands[command].TryGetValue(guild, out var commandInstance)) continue;
				commandInstance.DeleteAsync().Wait();
			}
		}

		static(bool, ApplicationCommandOptionType)? AsOptionType(this Type type)
		{
			if (type == typeof(bool))
				return (true, ApplicationCommandOptionType.Boolean);
			if (type == typeof(Optional<bool>))
				return (false, ApplicationCommandOptionType.Boolean);

			if (type == typeof(IChannel))
				return (true, ApplicationCommandOptionType.Channel);
			if (type == typeof(Optional<IChannel>))
				return (false, ApplicationCommandOptionType.Channel);

			if (type == typeof(long))
				return (true, ApplicationCommandOptionType.Integer);
			if (type == typeof(Optional<long>))
				return (false, ApplicationCommandOptionType.Integer);

			if (type == typeof(double))
				return (true, ApplicationCommandOptionType.Number);
			if (type == typeof(Optional<double>))
				return (false, ApplicationCommandOptionType.Number);

			if (type == typeof(IRole))
				return (true, ApplicationCommandOptionType.Role);
			if (type == typeof(Optional<IRole>))
				return (false, ApplicationCommandOptionType.Role);

			if (type == typeof(string))
				return (true, ApplicationCommandOptionType.String);
			if (type == typeof(Optional<string>))
				return (false, ApplicationCommandOptionType.String);

			if (type == typeof(IUser))
				return (true, ApplicationCommandOptionType.User);
			if (type == typeof(Optional<IUser>))
				return (false, ApplicationCommandOptionType.User);

			return null;
		}
	}
}
