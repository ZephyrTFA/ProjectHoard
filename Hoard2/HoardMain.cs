﻿using System.Reflection;

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

		public static readonly IReadOnlyList<string> SystemModules = new List<string>
		{
			"ModuleManager",
		}.AsReadOnly();

		public static void RestartWorker() => StopWorker(0);

		public static void StopWorker(int exitCode = 1)
		{
			Environment.ExitCode = exitCode;
			Shutdown().Wait();
			HoardHost.StopAsync(CancellationToken.None);
		}

		public static void Initialize(ILogger<Worker> log, CancellationToken workerToken)
		{
			HoardToken = workerToken;
			DiscordClient = new DiscordSocketClient(new DiscordSocketConfig
			{
				GatewayIntents = GatewayIntents.All,
			});
			Logger = log;
			DiscordClient.Log += HandleDiscordLog;
			DataDirectory = new DirectoryInfo($"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}/ProjectHoard");
			if (!DataDirectory.Exists)
				DataDirectory.Create();
		}

		static Task HandleDiscordLog(LogMessage message)
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
				if (Environment.GetEnvironmentVariable("PROJECT_HOARD_TOKEN", EnvironmentVariableTarget.Process) is { } envToken)
				{
					token = envToken;
				}
				else
				{
					Logger.LogCritical("Must populate '{TokenFile}' on disk or PROJECT_HOARD_TOKEN as an env var", tokenFile);
					return false;
				}
			}
			else
			{
				token = await File.ReadAllTextAsync(tokenFile);
			}

			DiscordClient.Ready += OnReady;
			await DiscordClient.LoginAsync(TokenType.Bot, token.Trim('\n', ' ', '\t', '\r'));
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
			await DiscordClient.CurrentUser.ModifyAsync(properties => properties.Username = "Project Hoard");

			await RestoreModules();

			await DiscordClient.SetStatusAsync(UserStatus.Online);
			Logger.LogInformation("Hoard Ready");
			return true;
		}

		public static async Task Shutdown()
		{
			if (DiscordClient.ConnectionState != ConnectionState.Connected)
				return;
			await DiscordClient.SetStatusAsync(UserStatus.Offline);
			await DiscordClient.StopAsync();
		}

		public static void LinkEvents()
		{
			DiscordClient.MessageReceived += ModuleHelper.DiscordClientOnMessageReceived;
			DiscordClient.MessageDeleted += ModuleHelper.DiscordClientOnMessageDeleted;
			DiscordClient.MessageUpdated += ModuleHelper.DiscordClientOnMessageUpdated;
			DiscordClient.UserJoined += ModuleHelper.DiscordClientOnUserJoined;
			DiscordClient.UserLeft += ModuleHelper.DiscordClientOnUserLeft;
			DiscordClient.SlashCommandExecuted += CommandHelper.ProcessApplicationCommand;
			DiscordClient.JoinedGuild += ModuleHelper.JoinedGuild;
			DiscordClient.LeftGuild += ModuleHelper.LeftGuild;
		}

		public static string GetGuildConfigFolder(ulong guild) => DataDirectory.CreateSubdirectory(guild.ToString()).FullName;

		public static async Task RestoreModules()
		{
			var allCommands = (await DiscordClient.GetGlobalApplicationCommandsAsync()).ToList();
			foreach (var guild in DiscordClient.Guilds)
				allCommands.AddRange(await guild.GetApplicationCommandsAsync());
			foreach (var command in allCommands)
				await command.DeleteAsync(new RequestOptions { RetryMode = RetryMode.RetryRatelimit });

			ModuleHelper.ModuleTypes.Clear();
			ModuleHelper.LoadAssembly(Assembly.GetExecutingAssembly(), out _);
			ModuleHelper.RestoreModules();
		}
	}
}
