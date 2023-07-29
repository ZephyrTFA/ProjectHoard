using System.Net.Http.Headers;
using System.Text;

using Discord;
using Discord.WebSocket;

using Hoard2.Util;

using Tgstation.Server.Api.Models;
using Tgstation.Server.Api.Models.Response;
using Tgstation.Server.Client;
using Tgstation.Server.Client.Components;

namespace Hoard2.Module.Builtin.SS13
{
	public class TgsServerInformation
	{
		public string ServerAddress { get; set; } = String.Empty;

		public Uri ServerUri => new Uri(ServerAddress);

		public long DefaultInstance { get; set; }
	}

	public class TGSLink : ModuleBase
	{
		public override List<Type> GetConfigKnownTypes() => new List<Type>
		{
			typeof(TgsServerInformation),
			typeof(Dictionary<ulong, (string, string)>),
			typeof((string, string)),
		};

		public TGSLink(string configPath) : base(configPath) { }

		public TgsServerInformation GetServerInformation(ulong guild) => GuildConfig(guild).Get("server-info", new TgsServerInformation())!;

		public void SetServerInformation(ulong guild, TgsServerInformation info) => GuildConfig(guild).Set("server-info", info);

		Dictionary<ulong, TokenResponse> _userTokenMap = new Dictionary<ulong, TokenResponse>();
		Dictionary<ulong, IServerClient> _userClientMap = new Dictionary<ulong, IServerClient>();
		ServerClientFactory _userTgsClientFactory = new ServerClientFactory(new ProductHeaderValue("ProjectHoard-TgsLink"));

		async Task<IServerClient?> GetUserTgsClient(Uri server, IGuildUser user)
		{
			if (_userClientMap.TryGetValue(user.Id, out var existingClient))
				if (existingClient.Token.ExpiresAt.CompareTo(DateTimeOffset.Now) > 0)
					return existingClient;

			var storedLoginMap = GuildConfig(user.GuildId).Get("user-login-store", new Dictionary<ulong, (string, string)>());
			if (storedLoginMap!.TryGetValue(user.Id, out var info))
				if (await DoUserLogin(server, user, info.Item1, info.Item2))
					return _userClientMap[user.Id];

			return null;
		}

		async Task<bool> DoUserLogin(Uri server, IUser user, string username, string password)
		{
			var userClient = await _userTgsClientFactory.CreateFromLogin(server, username, password);
			if (userClient.Token.Bearer is null) return false;
			_userClientMap[user.Id] = userClient;
			return true;
		}

		[ModuleCommand(GuildPermission.Administrator)]
		[CommandGuildOnly]
		public async Task SetServerAddress(SocketSlashCommand command, string address)
		{
			var serverInfo = GetServerInformation(command.GuildId!.Value);
			serverInfo.ServerAddress = address;
			SetServerInformation(command.GuildId.Value, serverInfo);
			await command.RespondAsync("Updated the server address.");
		}

		[ModuleCommand(GuildPermission.Administrator)]
		[CommandGuildOnly]
		public async Task SetDefaultInstance(SocketSlashCommand command, long defaultId)
		{
			var info = GetServerInformation(command.GuildId!.Value);
			info.DefaultInstance = defaultId;
			SetServerInformation(command.GuildId.Value, info);
			await command.RespondAsync("Updated the default instance id.");
		}

		public void UpdateStoredLoginInformation(IGuildUser user, string username, string password)
		{
			var guildConfig = GuildConfig(user.GuildId);
			var map = guildConfig.Get("user-login-store", new Dictionary<ulong, (string, string)>());
			map![user.Id] = (username, password);
			guildConfig.Set("user-login-store", map);
		}

		public(string, string)? GetStoredLoginInformation(IGuildUser user)
		{
			var guildConfig = GuildConfig(user.GuildId).Get("user-login-store", new Dictionary<ulong, (string, string)>());
			if (guildConfig!.TryGetValue(user.Id, out var value))
				return value;
			return null;
		}

		[ModuleCommand]
		[CommandGuildOnly]
		public async Task Login(SocketSlashCommand command, string username, string password, bool storeLoginInformation = false)
		{
			await command.DeferAsync(ephemeral: true);
			var serverInfo = GetServerInformation(command.GuildId!.Value);
			if (await DoUserLogin(serverInfo.ServerUri, command.User, username, password))
			{
				if (storeLoginInformation)
					UpdateStoredLoginInformation((IGuildUser)command.User, username, password);
				await command.SendOrModifyOriginalResponse("Logged in.");
			}
			else
				await command.SendOrModifyOriginalResponse("Failed to login.");
		}

		public static async Task<IInstanceClient> GetInstanceById(IServerClient client, long instanceId) =>
			client.Instances.CreateClient(await client.Instances.GetId(new EntityId { Id = instanceId }, default));

		[ModuleCommand]
		[CommandGuildOnly]
		public async Task GetActiveTestMerges(SocketSlashCommand command, long instanceId = -1)
		{
			var serverInfo = GetServerInformation(command.GuildId!.Value);
			if (await GetUserTgsClient(serverInfo.ServerUri, (IGuildUser)command.User) is not { } client)
			{
				await command.RespondAsync("You must login first.");
				return;
			}

			await command.DeferAsync();
			if (instanceId is -1)
				instanceId = serverInfo.DefaultInstance;
			var instance = await GetInstanceById(client, instanceId);

			var repository = await instance.Repository.Read(default);
			if (repository.RevisionInformation is null)
			{
				await command.SendOrModifyOriginalResponse("No revision information found.");
				return;
			}

			var testMerges = repository.RevisionInformation.ActiveTestMerges?.ToList() ?? new List<TestMerge>();
			if (!testMerges.Any())
			{
				await command.SendOrModifyOriginalResponse("No test merges.");
				return;
			}

			var longestPrNum = testMerges.Max(tm => tm.Number).ToString().Length;
			var responseBuilder = new StringBuilder("Active Test Merges:\n```\n");
			foreach (var testMergeInfo in testMerges)
			{
				var title = testMergeInfo.TitleAtMerge ?? "NO TITLE";
				if (title.Length > 64)
					title = title[..64];
				responseBuilder.AppendLine($"#{testMergeInfo.Number.ToString($"D{longestPrNum}")} | {title}");
				responseBuilder.AppendLine($"\t- @{testMergeInfo.TargetCommitSha ?? "HEAD"}");
			}
			responseBuilder.AppendLine("```");

			await command.SendOrModifyOriginalResponse(responseBuilder.ToString());
		}

		[ModuleCommand]
		[CommandGuildOnly]
		public async Task DreamDaemonPanel(SocketSlashCommand command, long instance = -1)
		{
			var serverInfo = GetServerInformation(command.GuildId!.Value);
			if (instance is -1) instance = serverInfo.DefaultInstance;

			if (await GetUserTgsClient(serverInfo.ServerUri, (IGuildUser)command.User) is not { } client)
			{
				await command.RespondAsync("Login first.");
				return;
			}

			await command.RespondAsync("caching");
			var _ = DoDreamDaemonPanel(await command.GetOriginalResponseAsync(), (IGuildUser)command.User, await GetInstanceById(client, instance));
		}

		// (user, instance) -> (channel, message)
		Dictionary<(ulong, long), (ulong, ulong)> _daemonPanelStore = new Dictionary<(ulong, long), (ulong, ulong)>();

		async Task<IUserMessage?> GetDaemonPanelMessage(IUser user, long instance)
		{
			if (!_daemonPanelStore.TryGetValue((user.Id, instance), out var match))
				return null;
			
			var channel = await HoardMain.DiscordClient.GetChannelAsync(match.Item1);
			if (channel is not IMessageChannel messageChannel)
				return null;

			return await messageChannel.GetMessageAsync(match.Item2) as IUserMessage;
		}

		public async Task DoDreamDaemonPanel(IUserMessage holder, IGuildUser user, IInstanceClient instanceClient, bool kill = false)
		{
			var currentState = await instanceClient.DreamDaemon.Read(default);
			if (kill)
			{
				await holder.DeleteAsync();
				return;
			}

			var existing = await GetDaemonPanelMessage(user, (long)instanceClient.Metadata.Id!);
			if (existing is {} && existing.Id != holder.Id)
				await existing.DeleteAsync();
			_daemonPanelStore[(user.Id, (long)instanceClient.Metadata.Id)] = (holder.Channel.Id, holder.Id);
			
			var embedData = new EmbedBuilder()
				.WithTitle($"Dream Daemon - {instanceClient.Metadata.Name!}")
				.WithColor(currentState.Status! switch
				{
					WatchdogStatus.Offline => Color.Red,
					WatchdogStatus.Restoring => Color.Purple,
					WatchdogStatus.Online => Color.Green,
					WatchdogStatus.DelayedRestart => Color.Orange,
					_ => Color.DarkGrey,
				})
				.AddField("Watchdog State", currentState.Status!.ToString());

			var shutdownButton = CreateButton($"dd-shutdown-{instanceClient.Metadata.Id}", user.Id)
				.WithLabel("Shutdown")
				.WithStyle(ButtonStyle.Danger)
				.WithDisabled(currentState.Status is WatchdogStatus.Offline)
				.Build();

			var launchButton = CreateButton($"dd-launch-{instanceClient.Metadata.Id}", user.Id)
				.WithLabel("Launch")
				.WithStyle(ButtonStyle.Success)
				.WithDisabled(currentState.Status is not WatchdogStatus.Offline)
				.Build();

			var componentBuilder = new ComponentBuilder()
				.AddRow(new ActionRowBuilder().WithComponents(new List<IMessageComponent> { launchButton, shutdownButton }));

			await holder.ModifyAsync(props =>
			{
				props.Content = "";
				props.Components = new Optional<MessageComponent>(componentBuilder.Build());
				props.Embeds = new Optional<Embed[]>(new[] { embedData.Build() });
			});
		}

		public override async Task OnButton(string buttonId, SocketMessageComponent button)
		{
			var data = buttonId.Split('-');
			switch (data[0])
			{
				case "dd":
					var serverClient = await GetUserTgsClient(GetServerInformation(button.GuildId!.Value).ServerUri, (IGuildUser)button.User);
					if (serverClient is null)
					{
						await button.RespondAsync("You are not logged in.", ephemeral: true);
						return;
					}
					
					var instance = Int64.Parse(data[2]);
					var instanceClient = await GetInstanceById(serverClient, instance);
					switch (data[1])
					{
						case "shutdown":
							await button.RespondAsync("Shutting down...");
							await instanceClient.DreamDaemon.Shutdown(default);
							break;

						case "launch":
							await button.RespondAsync("Launching...");
							var jobResponse = await instanceClient.DreamDaemon.Start(default);
							do
								jobResponse = await instanceClient.Jobs.GetId(jobResponse, default);
							while (jobResponse.StoppedAt is null);
							break;

						default:
							throw new NotImplementedException($"Unknown dd command: {data[1]}");
					}

					var message = (await GetDaemonPanelMessage(button.User, instance))!;
					var _ = DoDreamDaemonPanel(message, (IGuildUser)button.User, instanceClient);
					break;

				default:
					throw new NotImplementedException($"Unknown button handler: {data[0]}");
			}
		}
	}
}
