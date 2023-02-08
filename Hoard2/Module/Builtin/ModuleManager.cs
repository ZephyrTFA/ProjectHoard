using Discord;
using Discord.WebSocket;

namespace Hoard2.Module.Builtin
{
	public class ModuleManager : ModuleBase
	{
		public ModuleManager(string configPath) : base(configPath) { }

		[ModuleCommand("load-module", "Load a module", GuildPermission.Administrator)]
		public static async Task LoadModule(SocketSlashCommand command, string moduleId)
		{
			async Task LoadModuleActual()
			{
				if (!ModuleHelper.LoadModule(command.GuildId!.Value, moduleId, out var failReason))
					await command.ModifyOriginalResponse($"Failed to load module: {failReason}");
				else
					await command.ModifyOriginalResponse("Loaded module.");
			}

			await command.RespondAsync("Loading...");
			_ = CommandHelper.RunLongCommandTask(LoadModuleActual, await command.GetOriginalResponseAsync());
		}

		[ModuleCommand("unload-module", "Unload a module", GuildPermission.Administrator)]
		public static async Task UnloadModule(SocketSlashCommand command, string moduleId)
		{
			async Task UnloadModuleActual()
			{
				if (!ModuleHelper.UnloadModule(command.GuildId!.Value, moduleId, out var failReason))
					await command.ModifyOriginalResponse($"Failed to unload module: {failReason}");
				else
					await command.ModifyOriginalResponse("Unloaded module.");
			}

			await command.RespondAsync("Unloading...");
			_ = CommandHelper.RunLongCommandTask(UnloadModuleActual, await command.GetOriginalResponseAsync());
		}
	}
}
