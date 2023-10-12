using System.Reflection;
using Discord;
using Discord.WebSocket;
using Hoard2.Module;

namespace Hoard2
{
    public static class HoardMain
    {
        public static IHost HoardHost = null!;
        public static ILogger<Worker> Logger = null!;
        public static DiscordSocketClient DiscordClient = null!;
        public static DirectoryInfo DataDirectory = null!;
        public static CancellationToken HoardToken;

        public static void RestartWorker() => StopWorker(0);

        public static void StopWorker(int exitCode = 1)
        {
            Environment.ExitCode = exitCode;
            Shutdown().Wait();
            HoardHost.StopAsync(CancellationToken.None);
        }

        public static Task Initialize(ILogger<Worker> log, CancellationToken workerToken)
        {
            HoardToken = workerToken;
            DiscordClient = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.All ^
                                 (GatewayIntents.GuildPresences | GatewayIntents.GuildScheduledEvents),
                DefaultRetryMode = RetryMode.RetryRatelimit,
                MessageCacheSize = 2000,
                AlwaysDownloadUsers = true,
                LogLevel = LogSeverity.Verbose,
            });
            Logger = log;
            DiscordClient.Log += HandleDiscordLog;
            DataDirectory =
                new DirectoryInfo(
                    $"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}/ProjectHoard");
            if (!DataDirectory.Exists)
                DataDirectory.Create();
            return Task.CompletedTask;
        }

        private static Task HandleDiscordLog(LogMessage message)
        {
            var level = message.Severity switch
            {
                LogSeverity.Critical => LogLevel.Critical,
                LogSeverity.Debug => LogLevel.Debug,
                LogSeverity.Error => LogLevel.Error,
                LogSeverity.Info => LogLevel.Information,
                LogSeverity.Verbose => LogLevel.Trace,
                LogSeverity.Warning => LogLevel.Warning,
                _ => LogLevel.None,
            };

            // ignore TaskCanceledExceptions, it'll happen for gateway activities that take too long and when shutting down
            if (message.Exception is not TaskCanceledException)
                Logger.Log(level, message.Exception, "Discord: {}", message.Message);
            return Task.CompletedTask;
        }

        public static async Task<bool> Start()
        {
            LinkEvents();

            var ready = false;
            Task OnReady() => Task.FromResult(ready = true);

            string token;
            var tokenFile = Path.Join(DataDirectory.FullName, "hoard.token");
            if (!File.Exists(tokenFile))
            {
                if (Environment.GetEnvironmentVariable("PROJECT_HOARD_TOKEN", EnvironmentVariableTarget.Process) is
                    { } envToken)
                {
                    token = envToken;
                }
                else
                {
                    Logger.LogCritical("Must populate '{TokenFile}' on disk or PROJECT_HOARD_TOKEN as an env var",
                        tokenFile);
                    return false;
                }
            }
            else
            {
                token = await File.ReadAllTextAsync(tokenFile);
            }

            DiscordClient.Ready += OnReady;
            await DiscordClient.LoginAsync(TokenType.Bot, token.Trim('\n', ' ', '\t', '\r'));
            if (DiscordClient.LoginState != LoginState.LoggedIn)
            {
                Logger.LogCritical("Failed to login, is the token invalid?");
                Environment.Exit(1);
            }

            await DiscordClient.StartAsync();

            var checksLeft = 5;
            while (--checksLeft > 0 && !ready)
                await Task.Delay(1000);
            DiscordClient.Ready -= OnReady;

            if (!ready)
            {
                Logger.LogCritical("Failed to establish connection to Discord!");
                await DiscordClient.StopAsync();
                await DiscordClient.DisposeAsync();
                return false;
            }

            await DiscordClient.SetStatusAsync(UserStatus.DoNotDisturb);

            ModuleHelper.ModuleDataStorageDirectory = DataDirectory.CreateSubdirectory("module_data");
            ModuleHelper.CacheAssembly(Assembly.GetExecutingAssembly());
            ModuleHelper.RestoreGuildModules();
            ModuleHelper.AssertInnateModules();

            await CommandHelper.RefreshCommands();

            await DiscordClient.SetStatusAsync(UserStatus.Online);
            Logger.LogInformation("Hoard Ready");
            return true;
        }

        public static async Task Shutdown()
        {
            if (DiscordClient.LoginState != LoginState.LoggedIn)
                return;
            await DiscordClient.SetStatusAsync(UserStatus.Invisible);
            await CommandHelper.ClearCommandsForShutdown();
            await DiscordClient.StopAsync();
        }

        public static void LinkEvents()
        {
            DiscordClient.MessageReceived += ModuleHelper.DiscordClientOnMessageReceived;
            DiscordClient.MessageDeleted += ModuleHelper.DiscordClientOnMessageDeleted;
            DiscordClient.MessagesBulkDeleted += ModuleHelper.DiscordClientOnMessagesBulkDeleted;
            DiscordClient.MessageUpdated += ModuleHelper.DiscordClientOnMessageUpdated;
            DiscordClient.UserJoined += ModuleHelper.DiscordClientOnUserJoined;
            DiscordClient.UserLeft += ModuleHelper.DiscordClientOnUserLeft;
            DiscordClient.UserUpdated += ModuleHelper.DiscordClientOnUserUpdated;
            DiscordClient.GuildMemberUpdated += ModuleHelper.DiscordClientOnGuildMemberUpdated;
            DiscordClient.JoinedGuild += ModuleHelper.DiscordClientOnJoinedGuild;
            DiscordClient.LeftGuild += ModuleHelper.DiscordClientOnLeftGuild;
            DiscordClient.UserBanned += ModuleHelper.DiscordClientOnUserBanned;
            DiscordClient.UserUnbanned += ModuleHelper.DiscordClientOnUserUnbanned;
            DiscordClient.InviteCreated += ModuleHelper.DiscordClientOnInviteCreated;
            DiscordClient.InviteDeleted += ModuleHelper.DiscordClientOnInviteDeleted;
            DiscordClient.ButtonExecuted += ModuleHelper.DiscordClientOnButtonExecuted;
            DiscordClient.SelectMenuExecuted += ModuleHelper.DiscordClientOnSelectMenuExecuted;
            DiscordClient.SlashCommandExecuted += CommandHelper.DiscordClientOnSlashCommandExecuted;
        }
    }
}
