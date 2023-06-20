using Discord;
using Discord.WebSocket;

using Hoard2.Util;

namespace Hoard2.Module.Builtin
{
	public class ModuleManager : ModuleBase
	{
		public ModuleManager(string configPath) : base(configPath) { }

		[ModuleCommand(GuildPermission.Administrator)]
		[CommandGuildOnly]
		public async Task UnloadModule(SocketSlashCommand command, string moduleID)
		{
			await command.DeferAsync();
			if (!ModuleHelper.IsModuleLoaded(command.GuildId!.Value, ModuleHelper.TypeMap[moduleID]))
			{
				await command.SendOrModifyOriginalResponse($"Module `{moduleID}` is not loaded.");
				return;
			}

			ModuleHelper.UnloadModule(command.GuildId!.Value, ModuleHelper.TypeMap[moduleID]);
			await CommandHelper.RefreshCommands(command.GuildId.Value);
			await command.SendOrModifyOriginalResponse($"Unloaded module `{moduleID}`.");
		}

		[ModuleCommand(GuildPermission.Administrator)]
		[CommandGuildOnly]
		public static async Task LoadModule(SocketSlashCommand command, string moduleID)
		{
			await command.DeferAsync();

			switch (ModuleHelper.TryLoadModule(command.GuildId!.Value, moduleID, out var exception, out var failReason))
			{
				case ModuleLoadResult.Loaded:
					await CommandHelper.RefreshCommands(command.GuildId.Value);
					await command.SendOrModifyOriginalResponse($"Module `{moduleID}` loaded.");
					break;

				case ModuleLoadResult.AlreadyLoaded:
					await command.SendOrModifyOriginalResponse($"Module `{moduleID}` is already loaded.");
					break;

				case ModuleLoadResult.LoadErrored:
					await command.SendOrModifyOriginalResponse($"Failed to load module `{moduleID}`:\n```\n{exception}\n```");
					break;

				case ModuleLoadResult.LoadFailed:
					await command.SendOrModifyOriginalResponse($"Failed to load module `{moduleID}`: `{failReason}`");
					break;

				default:
				case ModuleLoadResult.NotFound:
					await command.SendOrModifyOriginalResponse($"Failed to load module `{moduleID}`: `unable to find module name in map`");
					break;
			}
		}
	}
}
