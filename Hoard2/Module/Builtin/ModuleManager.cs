using Discord;
using Discord.WebSocket;

namespace Hoard2.Module.Builtin
{
	public class ModuleManager : ModuleBase
	{
		public ModuleManager(ulong guildId, string configPath) : base(guildId, configPath) { }

		[ModuleCommand("load-hoard-module", "Load a module", GuildPermission.Administrator,
			new[] { "module-id" },
			new[] { typeof(string) },
			new[] { "ModuleID to load" })]
		public static async Task LoadModule(SocketSlashCommand command)
		{
			await command.RespondAsync("Loading...");

			if (!HoardMain.LoadModule(command.GuildId!.Value, (string)command.Data.Options.First(opt => opt.Name.Equals("module-id")).Value, out var failReason))
				await command.ModifyOriginalResponseAsync(properties => properties.Content = $"Failed to load module: {failReason}");
			else
				await command.RespondAsync("Loaded module.");
		}

		[ModuleCommand("unload-hoard-module", "Unload a module", GuildPermission.Administrator,
			new[] { "module-id" },
			new[] { typeof(string) },
			new[] { "ModuleID to unload" })]
		public static async Task UnloadModule(SocketSlashCommand command)
		{
			await command.RespondAsync("Unloading...");

			if (!HoardMain.UnloadModule(command.GuildId!.Value, (string)command.Data.Options.First(opt => opt.Name.Equals("module-id")).Value, out var failReason))
				await command.ModifyOriginalResponseAsync(properties => properties.Content = $"Failed to load module: {failReason}");
			else
				await command.RespondAsync("Loaded module.");
		}
	}
}
