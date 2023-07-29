using Discord;
using Discord.WebSocket;

using Hoard2.Util;

namespace Hoard2.Module
{
	public static class CommandHelper
	{
		static readonly Dictionary<Type, ModuleCommandMap> CommandMaps = new Dictionary<Type, ModuleCommandMap>();

		public static void ParseModuleCommands(Type module)
		{
			var map = ModuleCommandMap.GenerateCommandMap(module);
			if (map.Commands.Count == 0)
				CommandMaps.Remove(module);
			else
				CommandMaps[module] = map;
		}

		public static void DropModuleCommands(Type module) => CommandMaps.Remove(module);

		public static SlashCommandBuilder GenerateGuildCommandBuilder(ulong guild)
		{
			var builder = new SlashCommandBuilder()
				.WithName("project-hoard")
				.WithDescription("Master command for all modules.");
			foreach (var map in CommandMaps.Values.Where(module => ModuleHelper.IsModuleLoaded(guild, module.Module)))
				builder.AddOption(map.IntoBuilder());
			return builder;
		}

		public static async Task ClearCommandsForShutdown()
		{
			foreach (var socketApplicationCommand in await HoardMain.DiscordClient.GetGlobalApplicationCommandsAsync())
				await socketApplicationCommand.DeleteAsync();

			foreach (var guild in HoardMain.DiscordClient.Guilds.Select(guild => HoardMain.DiscordClient.GetGuild(guild.Id)))
				foreach (var guildCommand in await guild.GetApplicationCommandsAsync())
					await guildCommand.DeleteAsync();
		}

		public static async Task RefreshCommands(ulong target = 0)
		{
			if (target is not 0)
			{
				var guildActual = HoardMain.DiscordClient.GetGuild(target);
				foreach (var command in await guildActual.GetApplicationCommandsAsync())
					await command.DeleteAsync();
				var newCommand = GenerateGuildCommandBuilder(target);
				await guildActual.CreateApplicationCommandAsync(newCommand.Build());

				return;
			}

			foreach (var socketApplicationCommand in await HoardMain.DiscordClient.GetGlobalApplicationCommandsAsync())
				await socketApplicationCommand.DeleteAsync();

			foreach (var guildId in HoardMain.DiscordClient.Guilds.Select(guild => guild.Id))
				await RefreshCommands(guildId);
		}

		public static async Task DiscordClientOnSlashCommandExecuted(SocketSlashCommand slashCommand)
		{
			if (slashCommand.CommandName != "project-hoard")
				return;

			var moduleData = slashCommand.Data.Options.FirstOrDefault();
			if (moduleData?.Type != ApplicationCommandOptionType.SubCommandGroup)
				throw new Exception("Expected to receive a sub command group!");

			var moduleType = CommandMaps.Keys.FirstOrDefault(type => type.GetNormalizedRepresentation().Equals(moduleData.Name));
			if (moduleType is null)
				throw new Exception("Failed to find module type in map!");
			var moduleMap = CommandMaps[moduleType];

			var commandData = moduleData.Options.FirstOrDefault();
			if (commandData?.Type != ApplicationCommandOptionType.SubCommand)
				throw new Exception("Expected to receive a sub command!");

			var commandMap = moduleMap.Commands.FirstOrDefault(command => command.Name.Equals(commandData.Name));
			if (commandMap is null)
				throw new Exception("Failed to retrieve command map!");

			var guildId = slashCommand.GuildId ?? 0;
			if (guildId != 0)
			{
				if (commandMap.DmOnly)
				{
					await slashCommand.RespondAsync("This command can only be used in a DM.", ephemeral: true);
					return;
				}

				if (commandMap.Permission.HasValue)
				{

					var guildUser = (SocketGuildUser)slashCommand.User;
					if (!guildUser.GuildPermissions.Has(commandMap.Permission.Value))
					{
						await slashCommand.RespondAsync("You lack permission to do this.", ephemeral: true);
						return;
					}
				}
			}
			else
			{
				if (commandMap.GuildOnly)
				{
					await slashCommand.RespondAsync("This command can only be used in a Guild.", ephemeral: true);
					return;
				}
			}

			var paramArray = new object?[commandMap.Parameters.Count + 1];
			foreach (var param in commandData.Options)
			{
				var paramIdx = commandMap.Parameters.FindIndex(mapParam => mapParam.Name.Equals(param.Name));
				if (paramIdx == -1)
					throw new Exception($"Failed to map param {param} for {commandMap.Name}");
				paramArray[paramIdx + 1] = param.Value;
			}

			for (var i = 1; i < paramArray.Length; i++)
				paramArray[i] ??= commandMap.Parameters[i - 1].Default;
			paramArray[0] = slashCommand;

			var commandTask = (Task)commandMap.Caller.Invoke(ModuleHelper.InstanceMap[moduleType], paramArray)!;
			try
			{
				await commandTask.WaitAsync(TimeSpan.FromSeconds(5));
			}
			catch (TimeoutException)
			{
				HoardMain.Logger.LogWarning("A slash command has taken longer than five seconds to return control to the gateway!");
			}
			catch (Exception exception)
			{
				HoardMain.Logger.LogWarning(exception, "A slash command ('{}') experienced an exception during runtime", commandData.Name);
				await slashCommand.SendOrModifyOriginalResponse("An exception occurred during command processing.");
			}
		}

		public static Task DiscordClientOnSelectMenuExecuted(SocketMessageComponent menu) => throw new NotImplementedException();
	}
}
