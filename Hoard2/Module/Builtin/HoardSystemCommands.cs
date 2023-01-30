using Discord.WebSocket;

namespace Hoard2.Module.Builtin
{
	public class HoardSystemCommands : ModuleBase
	{
		public HoardSystemCommands(string configPath) : base(configPath) { }

		[ModuleCommand("restart-hoard", "restart hoard")]
		public static async Task RestartHoard(SocketSlashCommand command)
		{
			if (command.User.Id is not 946283057915232337)
			{
				await command.RespondAsync("Who are you?", ephemeral: true);
				return;
			}

			await command.RespondAsync("Restarting...");
			HoardMain.Logger.LogInformation("Restarting");
			HoardMain.RestartWorker();
		}

		[ModuleCommand("shutdown-hoard", "restart hoard")]
		public static async Task ShutdownHoard(SocketSlashCommand command)
		{
			if (command.User.Id is not 946283057915232337)
			{
				await command.RespondAsync("Who are you?", ephemeral: true);
				return;
			}

			await command.RespondAsync("Shutting down...");
			HoardMain.Logger.LogInformation("Shutting down");
			HoardMain.StopWorker();
		}


		public override bool TryLoad(ulong guild, out string reason)
		{
			reason = "not an operator guild";
			return guild is 837744059291533392;
		}
	}
}
