using Discord.WebSocket;

namespace Hoard2.Module.Builtin;

public class HoardSystemCommands : ModuleBase
{
    public readonly IReadOnlyList<ulong> OperatorGuilds = new List<ulong>
    {
        1106635532013940836,
        837744059291533392
    }.AsReadOnly();

    public HoardSystemCommands(string configPath) : base(configPath)
    {
    }

    [ModuleCommand]
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

    [ModuleCommand]
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

    [ModuleCommand]
    public static async Task ChangeUsername(SocketSlashCommand command, string username)
    {
        if (command.User.Id is not 946283057915232337)
        {
            await command.RespondAsync("Who are you?", ephemeral: true);
            return;
        }

        await HoardMain.DiscordClient.CurrentUser.ModifyAsync(props => { props.Username = username; });
        await command.RespondAsync("Updated");
    }

    public override bool TryLoad(ulong guild, out string reason)
    {
        reason = "not an operator guild";
        return OperatorGuilds.Contains(guild);
    }
}