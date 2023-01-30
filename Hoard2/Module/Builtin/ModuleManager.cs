using Discord;
using Discord.Rest;
using Discord.WebSocket;

namespace Hoard2.Module.Builtin
{
	public class ModuleManager : ModuleBase
	{
		public ModuleManager(string configPath) : base(configPath) { }

		[ModuleCommand("load-hoard-module", "Load a module", GuildPermission.Administrator)]
		public static async Task LoadModule(SocketSlashCommand command, string moduleId)
		{
			await command.RespondAsync("Loading...");

			_ = CommandHelper.RunLongCommandTask(
				LoadModuleActual(
					await command.GetOriginalResponseAsync(),
					command.GuildId!.Value,
					moduleId),
				await command.GetOriginalResponseAsync());
		}

		[ModuleCommand("unload-hoard-module", "Unload a module", GuildPermission.Administrator)]
		public static async Task UnloadModule(SocketSlashCommand command, string moduleId)
		{
			await command.RespondAsync("Unloading...");

			_ = CommandHelper.RunLongCommandTask(
				UnloadModuleActual(
					await command.GetOriginalResponseAsync(),
					command.GuildId!.Value,
					moduleId),
				await command.GetOriginalResponseAsync());
		}

		public static async Task LoadModuleActual(RestInteractionMessage originalMessage, ulong responseGuild, string moduleName)
		{
			if (!ModuleHelper.LoadModule(responseGuild, moduleName, out var failReason))
				await originalMessage.ModifyAsync(properties => properties.Content = $"Failed to load module: {failReason}");
			else
				await originalMessage.ModifyAsync(properties => properties.Content = "Loaded module.");
		}

		public static async Task UnloadModuleActual(RestInteractionMessage originalMessage, ulong responseGuild, string moduleName)
		{
			if (!ModuleHelper.UnloadModule(responseGuild, moduleName, out var failReason))
				await originalMessage.ModifyAsync(properties => properties.Content = $"Failed to unload module: {failReason}");
			else
				await originalMessage.ModifyAsync(properties => properties.Content = "Unloaded module.");
		}
	}
}
