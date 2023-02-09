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
				await ModuleHelper.LoadModule(command.GuildId!.Value, moduleId);
				await command.ModifyOriginalResponse("Module loaded.");
			}
			await command.RespondAsync("Loading...");
			_ = CommandHelper.RunLongCommandTask(LoadModuleActual, await command.GetOriginalResponseAsync());
		}

		[ModuleCommand("unload-module", "Unload a module", GuildPermission.Administrator)]
		public static async Task UnloadModule(SocketSlashCommand command, string moduleId)
		{
			async Task UnloadModuleActual()
			{
				await ModuleHelper.UnloadModule(command.GuildId!.Value, moduleId);
				await command.ModifyOriginalResponse("Module unloaded.");
			}
			await command.RespondAsync("Unloading...");
			_ = CommandHelper.RunLongCommandTask(UnloadModuleActual, await command.GetOriginalResponseAsync());
		}
	}
}
