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

		[ModuleCommand("Wipe and refresh all module commands")]
		public static async Task WipeAndRefreshModuleCommands(SocketSlashCommand command)
		{
			if (command.User.Id is not 946283057915232337)
			{
				await command.RespondAsync("Who are you?", ephemeral: true);
				return;
			}

			await command.RespondAsync("Working...");
			var task = new Task(() =>
			{
				CommandHelper.WipeAllGuildCommands(command).Wait();
				CommandHelper.RefreshAllGuildCommands().Wait();
			});
			_ = task.ContinueWith(_ =>
			{
				if (task.IsCompletedSuccessfully)
					command.ModifyOriginalResponse("Done.").Wait();
				else
					command.ModifyOriginalResponse($"Failed: {task.Exception!.Message}").Wait();
			});
			task.Start();
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
			await CommandHelper.WipeAllGuildCommands();
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
