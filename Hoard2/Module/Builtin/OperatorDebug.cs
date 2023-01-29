using Discord;
using Discord.WebSocket;

namespace Hoard2.Module.Builtin
{
	public class OperatorDebug : ModuleBase
	{
		public OperatorDebug(string configPath) : base(configPath) { }

		public override bool TryLoad(ulong guild, out string reason)
		{
			reason = "not an operator guild";
			return guild is 837744059291533392;
		}

		[ModuleCommand("debug-halting-command", "enqueues a command that tries to sleep for a minute", GuildPermission.Administrator)]
		public static async Task DebugHaltingCommand(SocketSlashCommand command)
		{
			await command.RespondAsync("Halting...");
			await Task.Delay(TimeSpan.FromMinutes(10));
			await command.ModifyOriginalResponseAsync(prop => prop.Content = "A halting warning should have been issued.");
		}
	}
}
