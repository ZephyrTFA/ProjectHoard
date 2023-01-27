using Discord;
using Discord.WebSocket;

namespace Hoard2.Module.Builtin
{
	public class ModuleManager : ModuleBase
	{
		public ModuleManager(string configPath) : base(configPath) { }

		[ModuleCommand("load-hoard-module", "Load a module", GuildPermission.Administrator,
			new[] { "module-id" },
			new[] { typeof(string) },
			new[] { "ModuleID to load" })]
		public static async Task LoadModule(SocketSlashCommand command)
		{
			await command.RespondAsync("Loading...");

			if (!ModuleHelper.LoadModule(command.GuildId!.Value, (string)command.Data.Options.First(opt => opt.Name.Equals("module-id")).Value, out var failReason))
				await command.ModifyOriginalResponseAsync(properties => properties.Content = $"Failed to load module: {failReason}");
			else
				await command.ModifyOriginalResponseAsync(properties => properties.Content = "Loaded module.");
		}

		[ModuleCommand("unload-hoard-module", "Unload a module", GuildPermission.Administrator,
			new[] { "module-id" },
			new[] { typeof(string) },
			new[] { "ModuleID to unload" })]
		public static async Task UnloadModule(SocketSlashCommand command)
		{
			await command.RespondAsync("Unloading...");

			if (!ModuleHelper.UnloadModule(command.GuildId!.Value, (string)command.Data.Options.First(opt => opt.Name.Equals("module-id")).Value, out var failReason))
				await command.ModifyOriginalResponseAsync(properties => properties.Content = $"Failed to load module: {failReason}");
			else
				await command.ModifyOriginalResponseAsync(properties => properties.Content = "Loaded module.");
		}
	}
}
