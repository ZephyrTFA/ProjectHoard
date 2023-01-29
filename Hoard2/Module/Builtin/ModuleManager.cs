using Discord;
using Discord.Rest;
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

			var _ = LoadModuleActual(
				await command.GetOriginalResponseAsync(),
				command.GuildId!.Value,
				(string)command.Data.Options.First(opt => opt.Name.Equals("module-id")).Value);
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

		[ModuleCommand("unload-hoard-module", "Unload a module", GuildPermission.Administrator,
			new[] { "module-id" },
			new[] { typeof(string) },
			new[] { "ModuleID to unload" })]
		public static async Task UnloadModule(SocketSlashCommand command)
		{
			await command.RespondAsync("Unloading...");

			var _ = UnloadModuleActual(
				await command.GetOriginalResponseAsync(),
				command.GuildId!.Value,
				(string)command.Data.Options.First(opt => opt.Name.Equals("module-id")).Value);
		}
	}
}
