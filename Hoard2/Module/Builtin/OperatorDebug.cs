using System.ComponentModel;

using Discord;
using Discord.WebSocket;

using Hoard2.Util;

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

		[ModuleCommand(GuildPermission.Administrator)]
		[Description("Tries to sleep for a 20 seconds.")]
		public static async Task DebugHaltingCommand(SocketSlashCommand command)
		{
			await command.SendOrModifyOriginalResponse("Halting...");
			await Task.Delay(TimeSpan.FromSeconds(20));
			await command.SendOrModifyOriginalResponse("A halting warning should have been issued.");
		}

		[ModuleCommand(GuildPermission.Administrator)]
		[Description("Throws an exception.")]
		public static async Task DebugExceptionCommand(SocketSlashCommand command)
		{
			await command.SendOrModifyOriginalResponse("Throwing...");
			throw new Exception("This is a test exception.");
		}
	}
}
