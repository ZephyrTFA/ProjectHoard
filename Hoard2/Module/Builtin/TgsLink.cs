using System.Buffers.Text;
using System.Text;

using Discord;
using Discord.WebSocket;

using Octokit;
using Octokit.Internal;

using Tgstation.Server.Api.Models;
using Tgstation.Server.Api.Models.Request;
using Tgstation.Server.Api.Models.Response;
using Tgstation.Server.Client;
using Tgstation.Server.Client.Components;

using ProductHeaderValue = System.Net.Http.Headers.ProductHeaderValue;
using Timer = System.Timers.Timer;

namespace Hoard2.Module.Builtin
{
	static class TgsHelpers
	{
		/// (SoftRestart, SoftShutdown)
		internal static string RestartType(this(bool, bool) pair)
		{
			if (pair is { Item1: false, Item2: false }) return "Nothing";
			if (pair.Item1) return "Restart";
			return "Shutdown";
		}

		internal static ButtonBuilder WithLocked(this ButtonBuilder builder, bool locked)
		{
			if (builder.IsDisabled)
				return builder;
			return builder.WithDisabled(locked);
		}

		internal static string ManglePassword(this string password)
		{
			var utf8 = Encoding.UTF8.GetBytes(password);
			var utfCount = utf8.Length;
			Array.Resize(ref utf8, Base64.GetMaxEncodedToUtf8Length(utfCount));
			Base64.EncodeToUtf8InPlace(utf8, utfCount, out _);
			return Encoding.UTF8.GetString(utf8);
		}

		internal static string DemanglePassword(this string mangled)
		{
			var utf8 = Encoding.UTF8.GetBytes(mangled);
			Base64.DecodeFromUtf8InPlace(utf8, out var written);
			Array.Resize(ref utf8, written);
			return Encoding.UTF8.GetString(utf8);
		}

		internal static bool IsExpired(this TokenResponse token) => token.ExpiresAt.CompareTo(DateTimeOffset.UtcNow) < 0;
	}

	public struct PanelStore
	{
		public TgsLink Module { get; init; }

		public SocketGuildUser User { get; init; }

		public long InstanceId { get; init; }

		public ulong ChannelId { get; init; }

		public ulong MessageId { get; init; }

		public ulong GuildId { get; init; }

		IInstanceClient? _backing;

		IInstanceClient InstanceClient
		{
			get
			{
				if (_backing is null)
				{
					var client = Module.GetUserTgsClient(User).GetAwaiter().GetResult();
					_backing = client.Instances.CreateClient(
						client.Instances.GetId(new EntityId { Id = InstanceId }, CancellationToken.None).GetAwaiter().GetResult());
				}
				return _backing;
			}
		}

		public void ResetClient() => _backing = null;

		public async Task<RepositoryResponse> GetRepository() => await InstanceClient.Repository.Read(CancellationToken.None);

		public async Task UpdateRepository(RepositoryUpdateRequest request) => await InstanceClient.Repository.Update(request, CancellationToken.None);

		public async Task<DreamDaemonResponse> GetDaemonInstance() => await InstanceClient.DreamDaemon.Read(CancellationToken.None);

		public async Task UpdateDaemon(DreamDaemonRequest request) => await InstanceClient.DreamDaemon.Update(request, CancellationToken.None);
	}

	public class TgsLink : ModuleBase
	{
		public static ServerClientFactory ClientFactory = new ServerClientFactory(new ProductHeaderValue("TgsLink"));
		List<Guid> _knownUniques = new List<Guid>();
		Dictionary<ulong, bool> _panelTimerLockout = new Dictionary<ulong, bool>();

		Dictionary<ulong, Timer> _panelTimers = new Dictionary<ulong, Timer>();
		Dictionary<SocketGuildUser, IServerClient> _tgsCache;

		Dictionary<ulong, Dictionary<string, TokenResponse>> _usernameTokenMap = new Dictionary<ulong, Dictionary<string, TokenResponse>>();
		Dictionary<ulong, PanelStore> _userPanels = new Dictionary<ulong, PanelStore>();
		public TgsLink(string configPath) : base(configPath)
		{
			_tgsCache = new Dictionary<SocketGuildUser, IServerClient>();
		}

		public string? GetUserTgsUsername(SocketGuildUser user)
		{
			var config = GuildConfig(user.Guild.Id);
			var map = config.Get("user-tgs-username-map", new Dictionary<ulong, string>())!;
			if (!map.ContainsKey(user.Id)) return null;
			return map[user.Id];
		}

		public void SetUserTgsUsername(SocketGuildUser user, string username)
		{
			var config = GuildConfig(user.Guild.Id);
			var map = config.Get("user-tgs-username-map", new Dictionary<ulong, string>())!;
			map[user.Id] = username;
			config.Set("user-tgs-username-map", map);
		}

		public async Task<Exception?> RefreshToken(SocketGuildUser user, string username, string? password = null)
		{
			var config = GuildConfig(user.Guild.Id);
			var usernamePassMap = config.Get<Dictionary<string, string>>("user-pass-map") ?? new Dictionary<string, string>();
			if (password is null && !usernamePassMap.ContainsKey(username)) return new Exception("Login information not saved");
			password ??= usernamePassMap[username].DemanglePassword();

			try
			{
				if (!config.TryGet<string>("server-address", out var serverAddress)) return new Exception("Server address not set");
				var client = await ClientFactory.CreateFromLogin(new Uri(serverAddress), username, password);
				_usernameTokenMap[user.Guild.Id][username] = client.Token;
				SetUserTgsUsername(user, username);
				return null;
			}
			catch (Exception exception)
			{
				return exception;
			}
		}

		[ModuleCommand("login to TGS")]
		public async Task UserLogin(SocketSlashCommand command, string username, string password, bool saveLoginInformation = false)
		{
			var guildId = command.GuildId!.Value;
			async Task DoLogin()
			{
				if (await RefreshToken((SocketGuildUser)command.User, username, password) is { } failReason)
					await command.ModifyOriginalResponseAsync(props => props.Content = $"Login failed: `{failReason.Message[..Math.Min(failReason.Message.Length, 200)]}`");
				else await command.ModifyOriginalResponseAsync(props => props.Content = "Logged in.");
				if (!saveLoginInformation) return;
				var config = GuildConfig(guildId);
				var usernamePassMap = config.Get<Dictionary<string, string>>("user-pass-map") ?? new Dictionary<string, string>();
				usernamePassMap[username] = password.ManglePassword();
				config.Set("user-pass-map", usernamePassMap);
			}

			await command.RespondAsync("Logging in...", ephemeral: true);
			_ = CommandHelper.RunLongCommandTask(DoLogin, await command.GetOriginalResponseAsync());
		}

		[ModuleCommand("set the address for TGS", GuildPermission.Administrator)]
		public async Task SetAddress(SocketSlashCommand command, string address)
		{
			try
			{
				_ = new Uri(address);
			}
			catch (Exception exception)
			{
				await command.RespondAsync($"Failed to parse address! (`{exception.Message}`)");
				return;
			}

			GuildConfig(command.GuildId!.Value).Set("server-address", address);
			await command.RespondAsync("Updated address");
		}

		[ModuleCommand("Set your personal github token")]
		public async Task SetUserGithubToken(SocketSlashCommand command, string? token)
		{
			var userTokens = GlobalConfig.Get("user-gh-tokens", new Dictionary<ulong, string>())!;
			if (token is null)
			{
				userTokens.Remove(command.User.Id);
				await command.RespondAsync("Removed your token.", ephemeral: true);
			}
			else
			{
				userTokens[command.User.Id] = token;
				await command.RespondAsync("Updated your token.", ephemeral: true);
			}
			GlobalConfig.Set("user-gh-tokens", userTokens);
		}

		[ModuleCommand("Get server info", GuildPermission.Administrator)]
		public async Task GetServerInfo(SocketSlashCommand command)
		{
			var config = GuildConfig(command.GuildId!.Value);
			await command.RespondAsync(embed: new EmbedBuilder()
				.WithTimestamp(DateTimeOffset.UtcNow)
				.WithTitle("TGSLink Server Information")
				.AddField("Address", $"`{config.Get<string>("server-address") ?? "No address set"}`")
				.AddField("Has Token", config.Get<string>("server-gh-token") is { } ? "True" : "False")
				.Build());
		}

		public override void OnLoad(ulong guild)
		{
			if (!_usernameTokenMap.ContainsKey(guild))
				_usernameTokenMap[guild] = new Dictionary<string, TokenResponse>();
		}

		string? GetUserGhToken(SocketUser user)
		{
			var tokens = GlobalConfig.Get("user-gh-tokens", new Dictionary<ulong, string>())!;
			return tokens.ContainsKey(user.Id) ? tokens[user.Id] : null;
		}

		GitHubClient GetUserGithubClient(SocketUser user)
		{
			if (GetUserGhToken(user) is { } token)
				return new GitHubClient(new Octokit.ProductHeaderValue("TgsLink"), new InMemoryCredentialStore(new Credentials(token)));
			return new GitHubClient(new Octokit.ProductHeaderValue("TgsLink"));
		}

		public async Task<IServerClient> GetUserTgsClient(SocketGuildUser user)
		{
			if (_tgsCache.TryGetValue(user, out var cached) && !cached.Token.IsExpired())
				return cached;

			var config = GuildConfig(user.Guild.Id);
			if (!config.TryGet<string>("server-address", out var serverAddress))
				throw new Exception("Server address not set");

			var username = GetUserTgsUsername(user);
			if (username is null)
				throw new UnauthorizedException("Must login first!");

			if (!_usernameTokenMap[user.Guild.Id].ContainsKey(username) && await RefreshToken(user, username) is { } exception)
				throw exception;

			var token = _usernameTokenMap[user.Guild.Id][username];
			if (token.IsExpired() && await RefreshToken(user, username) is { } exception1)
				throw exception1;

			return ClientFactory.CreateFromToken(new Uri(serverAddress), _usernameTokenMap[user.Guild.Id][username]);
		}

		public async Task<PullRequest[]> GetPulls(SocketGuildUser user, long repository, bool onlyMarkedTm = true)
		{
			var client = GetUserGithubClient(user);
			var pulls = await client.PullRequest.GetAllForRepository(repository);
			if (onlyMarkedTm)
				pulls = pulls.Where(pull => pull.Labels.Any(label => label.Name.ToLower().Contains("test merge candidate"))).ToList();
			var sorted = pulls
				.OrderBy(pullInfo => pullInfo.Labels.Any(label => label.Name.ToLower().Contains("test merge candidate")))
				.ThenBy(pullInfo => pullInfo.Id);
			return sorted.ToArray();
		}

		public async Task<PullRequest[]> GetPulls(SocketGuildUser user, string owner, string repository, bool onlyMarkedTM = true)
		{
			var client = GetUserGithubClient(user);
			var repoId = (await client.Repository.Get(owner, repository)).Id;
			return await GetPulls(user, repoId, onlyMarkedTM);
		}

		public async Task<InstanceResponse[]> GetTgsInstances(SocketGuildUser user)
		{
			var client = await GetUserTgsClient(user);
			var instances = await client.Instances.List(null, CancellationToken.None);
			return instances.ToArray();
		}

		public async Task<IInstanceClient> GetTgsInstance(SocketGuildUser user, long instanceId)
		{
			var client = await GetUserTgsClient(user);
			return client.Instances.CreateClient((await GetTgsInstances(user)).First(inst => inst.Id == instanceId));
		}

		public async Task<RepositoryResponse> GetTgsInstanceRepository(SocketGuildUser user, long instanceId)
		{
			var instanceClient = await GetTgsInstance(user, instanceId);
			return await instanceClient.Repository.Read(CancellationToken.None);
		}

		public async Task<TestMerge[]> GetTgsTestMerges(SocketGuildUser user, long instanceId)
		{
			var repository = await GetTgsInstanceRepository(user, instanceId);
			return repository.RevisionInformation?.ActiveTestMerges?.ToArray() ?? Array.Empty<TestMerge>();
		}

		[ModuleCommand("Check the active test merges on the given instance")]
		public async Task GetTestMerges(SocketSlashCommand command, long instanceId = 1)
		{
			async Task FetchTMs()
			{
				var tms = await GetTgsTestMerges((SocketGuildUser)command.User, instanceId);
				if (!tms.Any())
				{
					await command.ModifyOriginalResponse("No TMs.");
					return;
				}
				var response = new StringBuilder("Active TMs:\n```\n");
				var largest = tms.Max(tm => tm.Number);
				var padLength = Math.Floor(Math.Log10(largest) + 1);
				foreach (var tm in tms)
				{
					response.AppendLine($"#{tm.Number.ToString().PadLeft((int)padLength)} - {tm.TitleAtMerge![..Math.Min(tm.TitleAtMerge!.Length, 48)]}");
					response.AppendLine($" -@ {tm.TargetCommitSha}");
				}
				response.AppendLine("```");
				await command.Channel.SendMessageAsync(response.ToString());
			}

			await command.RespondAsync("Fetching...");
			_ = CommandHelper.RunLongCommandTask(FetchTMs, await command.GetOriginalResponseAsync());
		}

		[ModuleCommand("List the possible test merges")]
		public async Task ListAvailableTestMerges(SocketSlashCommand command, long instanceId = 1)
		{
			async Task Action()
			{
				var repository = await (await GetTgsInstance((SocketGuildUser)command.User, instanceId)).Repository.Read(CancellationToken.None);
				PullRequest[] openTestMergeMarked;
				try
				{
					openTestMergeMarked = await GetPulls((SocketGuildUser)command.User, repository.RemoteRepositoryOwner!, repository.RemoteRepositoryName!);
				}
				catch (RateLimitExceededException)
				{
					await command.RespondAsync("You are being rate limited by GitHub. Consider setting a token!", ephemeral: true);
					return;
				}

				var tmd = await GetTgsTestMerges((SocketGuildUser)command.User, instanceId);

				var tmdNumbers = tmd.Select(tm => tm.Number);
				var available = openTestMergeMarked.Where(pull => !tmdNumbers.Contains(pull.Number)).ToList();

				var response = new StringBuilder();
				response.AppendLine($"There are {available.Count} available PRs that are marked for test merge and not already merged:");
				if (available.Count == 0)
				{
					await command.ModifyOriginalResponse(response.ToString());
					return;
				}

				var buttons = new List<SelectMenuOptionBuilder>();
				var largest = available.Max(tm => tm.Number);
				var padLength = Math.Floor(Math.Log10(largest) + 1);
				response.AppendLine("```");
				foreach (var possible in available)
				{
					buttons.Add(new SelectMenuOptionBuilder().WithLabel($"#{possible.Number}"));
					response.AppendLine($"#{possible.Number.ToString().PadLeft((int)padLength)} - {possible.Title![..Math.Min(possible.Title!.Length, 48)]}");
				}
				response.AppendLine("```");
				await command.ModifyOriginalResponseAsync(resp =>
				{
					resp.Components = new ComponentBuilder().WithSelectMenu(new SelectMenuBuilder().WithOptions(buttons).WithCustomId("tgs-tm-menu")).Build();
					resp.Content = response.ToString();
				});
			}

			await command.RespondAsync("Working...");
			_ = CommandHelper.RunLongCommandTask(Action, await command.GetOriginalResponseAsync());
		}

		[ModuleCommand("Check and update DreamDaemon")]
		public async Task Daemon(SocketSlashCommand command, long instanceId = 1)
		{
			await command.RespondAsync("Fetching...");
			var original = await command.GetOriginalResponseAsync();
			var storeInfo = new PanelStore
			{
				Module = this,
				User = (SocketGuildUser)command.User,
				ChannelId = command.Channel.Id,
				GuildId = command.GuildId!.Value,
				InstanceId = instanceId,
				MessageId = original.Id,
			};
			_userPanels[command.User.Id] = storeInfo;
			_panelTimerLockout[command.User.Id] = false;
			await UpdateUserDaemonPanel(storeInfo);
		}

		async Task UpdateUserDaemonPanel(PanelStore storeInfo, bool @lock = false)
		{
			var channelInstance = await HoardMain.DiscordClient.GetChannelAsync(storeInfo.ChannelId) as IMessageChannel;
			await channelInstance!.ModifyMessageAsync(storeInfo.MessageId, props =>
			{
				props.Content = "";
				(props.Embed, props.Components) = DaemonPanel(storeInfo, @lock).GetAwaiter().GetResult();
			});
		}

		[ModuleCommand("Reset tracked state of the repository")]
		public async Task ResetRepo(SocketSlashCommand command, long instanceId = 1)
		{
			await command.RespondAsync("Working...");
			var tgsClient = await GetTgsInstance((SocketGuildUser)command.User, instanceId);
			var repo = await tgsClient.Repository.Read(CancellationToken.None);
			var resp = await tgsClient.Repository.Update(new RepositoryUpdateRequest
			{
				NewTestMerges = new List<TestMergeParameters>(),
				Reference = repo.Reference,
				UpdateFromOrigin = true,
			}, CancellationToken.None);
			if (resp.ActiveJob is { } job)
			{
				var checkCount = 0;
				do
				{
					checkCount++;
					await Task.Delay(1000);
					await command.ModifyOriginalResponseAsync(props => props.Content = $"Checking (#{checkCount})");
					job = await UpdateJob((SocketGuildUser)command.User, job, instanceId);
				}
				while (job.StoppedAt is null && checkCount < 10);
				if (job.StoppedAt is null)
				{
					await command.ModifyOriginalResponseAsync(props => props.Content = "Probably failed to process, job took too long to await!");
					return;
				}
				if (job.ExceptionDetails is { })
				{
					await command.ModifyOriginalResponseAsync(props => props.Content = $"Failed to process: `{job.ExceptionDetails}`");
					return;
				}
			}
			await command.ModifyOriginalResponseAsync(props => props.Content = "Success");
		}

		[ModuleCommand("View the test merge panel")]
		public async Task TestMergePanel(SocketSlashCommand command, long instanceId = 1)
		{
			await command.RespondAsync("Fetching...");
			await UpdateTestMergePanel((SocketGuildUser)command.User, instanceId, command.GuildId!.Value, await command.GetOriginalResponseAsync());
		}

		public async Task UpdateTestMergePanel(SocketGuildUser user, long instance, ulong guild, IUserMessage message, bool @lock = false)
		{
			SelectMenuBuilder menu;
			try
			{
				menu = await GetTestMergeMenu(user, instance, guild);
			}
			catch (RateLimitExceededException)
			{
				await message.ModifyAsync(props => props.Content = "You are being rate limited by GitHub, consider setting your token!");
				return;
			}
			catch (EndOfStreamException)
			{
				await message.ModifyAsync(props => props.Content = "There are no available/marked pulls.");
				return;
			}

			var cancelButton = GetButton($"tgs/test-merge-menu/{instance}/cancel", guild).WithStyle(ButtonStyle.Danger).WithLabel("Cancel").WithDisabled(@lock);
			var instanceMeta = await GetTgsInstance(user, instance);
			await message.ModifyAsync(props =>
			{
				props.Content = $"Test Merge Panel - {instanceMeta.Metadata.Name}";
				props.Components = new ComponentBuilder().WithSelectMenu(menu.WithDisabled(@lock)).WithButton(cancelButton).Build();
			});
		}
		async Task<(Embed, MessageComponent)> DaemonPanel(PanelStore storeInfo, bool @lock = false)
		{
			if (_panelTimerLockout.TryGetValue(storeInfo.User.Id, out var timerLocked) && timerLocked)
				@lock = true;

			var instance = await GetTgsInstance(storeInfo.User, storeInfo.InstanceId);
			var daemon = await instance.DreamDaemon.Read(CancellationToken.None);

			var embed = new EmbedBuilder().WithTitle($"Dream Daemon Panel - {instance.Metadata.Name}");
			var isOnline = daemon.Status != WatchdogStatus.Offline;
			embed.AddField("Watchdog State", isOnline ? "ONLINE" : "OFFLINE");

			var menu = new ComponentBuilder();
			menu.AddRow(new ActionRowBuilder()
				.WithButton(GetButton("tgs/refresh", storeInfo.GuildId)
					.WithLabel("Refresh Panel").WithStyle(ButtonStyle.Secondary).WithLocked(@lock))
				.WithButton(GetButton("tgs/cancel", storeInfo.GuildId)
					.WithLabel("Cancel").WithStyle(ButtonStyle.Danger).WithLocked(@lock)));

			var shutdownRow = new ActionRowBuilder();
			foreach (var button in await GetLaunchButtons(storeInfo))
				shutdownRow.WithButton(button.WithLocked(@lock));
			menu.AddRow(shutdownRow);

			if (isOnline)
			{
				embed.AddField("Restart Type", (daemon.SoftRestart ?? false, daemon.SoftShutdown ?? false).RestartType());
				var restartTypeRow = new ActionRowBuilder();
				foreach (var restartTypeButton in await GetRestartTypeButtons(storeInfo))
					restartTypeRow.WithButton(restartTypeButton.WithLocked(@lock));
				menu.AddRow(restartTypeRow);
			}

			if (_panelTimers.ContainsKey(storeInfo.User.Id))
			{
				var timer = _panelTimers[storeInfo.User.Id];
				timer.Stop();
				_panelTimers.Remove(storeInfo.User.Id);
				timer.Dispose();
			}

			var timeoutTimer = _panelTimers[storeInfo.User.Id] = new Timer(10000);
			timeoutTimer.Elapsed += (obj, args) =>
			{
				timeoutTimer.Stop();
				_panelTimers.Remove(storeInfo.User.Id);
				_panelTimerLockout[storeInfo.User.Id] = true;
				_ = UpdateUserDaemonPanel(storeInfo);
			};
			timeoutTimer.Start();
			return (embed.Build(), menu.Build());
		}

		async Task<ButtonBuilder[]> GetRestartTypeButtons(PanelStore storeInfo)
		{
			var instance = await storeInfo.GetDaemonInstance();
			var isGraceful = instance.SoftShutdown ?? false;
			var isRestart = instance.SoftRestart ?? false;
			var buttonGraceful = GetButton($"tgs/restart-type/{storeInfo.InstanceId}/shutdown", storeInfo.GuildId)
				.WithDisabled(isGraceful)
				.WithStyle(ButtonStyle.Danger)
				.WithLabel("Graceful");
			var buttonRestart = GetButton($"tgs/restart-type/{storeInfo.InstanceId}/restart", storeInfo.GuildId)
				.WithDisabled(isRestart)
				.WithStyle(ButtonStyle.Primary)
				.WithLabel("Restart");
			var buttonNothing = GetButton($"tgs/restart-type/{storeInfo.InstanceId}/nothing", storeInfo.GuildId)
				.WithDisabled(!isGraceful && !isRestart)
				.WithStyle(ButtonStyle.Secondary)
				.WithLabel("Nothing");
			return new[] { buttonGraceful, buttonRestart, buttonNothing };
		}

		async Task<SelectMenuBuilder> GetTestMergeMenu(SocketGuildUser user, long instanceId, ulong guildId)
		{
			var builder = GetMenu($"tgs/test-merge-menu/{instanceId}", guildId);

			var tgsClient = await GetTgsInstance(user, instanceId);
			var repo = await tgsClient.Repository.Read(CancellationToken.None);
			var existingTMs = (repo.RevisionInformation?.ActiveTestMerges ?? new List<TestMerge>()).Select(tm => tm.Number).ToList();

			var availableTMs = await GetPulls(user, repo.RemoteRepositoryOwner!, repo.RemoteRepositoryName!);
			var numAllowed = Math.Min(availableTMs.Length, 25);
			if (numAllowed == 0)
				throw new EndOfStreamException();
			builder
				.WithMaxValues(numAllowed)
				.WithMinValues(0)
				.WithOptions(availableTMs[..numAllowed].Select(avail =>
						new SelectMenuOptionBuilder()
							.WithLabel($"#{avail.Number} - {avail.Title[..Math.Min(avail.Title.Length, 80)]}")
							.WithDescription(avail.MergeCommitSha)
							.WithValue($"{avail.Number}")
							.WithDefault(existingTMs.Contains(avail.Number)))
					.ToList());

			return builder;
		}

		async Task<ButtonBuilder[]> GetLaunchButtons(PanelStore storeInfo)
		{
			var instance = await storeInfo.GetDaemonInstance();
			var canLaunch = instance.Status == WatchdogStatus.Offline;
			var canShutdown = instance.Status == WatchdogStatus.Online;

			var buttonLaunch = GetButton($"tgs/set-watchdog/{storeInfo.InstanceId}/launch", storeInfo.GuildId)
				.WithDisabled(!canLaunch)
				.WithStyle(ButtonStyle.Success)
				.WithLabel("Launch");
			var buttonShutdown = GetButton($"tgs/set-watchdog/{storeInfo.InstanceId}/shutdown", storeInfo.GuildId)
				.WithDisabled(!canShutdown)
				.WithStyle(ButtonStyle.Danger)
				.WithLabel("Shutdown");
			return new[] { buttonLaunch, buttonShutdown };
		}

		public override async Task OnMenu(SocketMessageComponent menu, string menuId)
		{
			if (menuId.StartsWith("tgs/"))
			{
				var split = menuId.Split("/");
				var command = split[1];
				var instance = Int64.Parse(split[2]);

				switch (command)
				{
					case "test-merge-menu":
						var repo = await GetTgsInstanceRepository((SocketGuildUser)menu.User, instance);
						var repoRequest = new RepositoryUpdateRequest
						{
							UpdateFromOrigin = true,
							Reference = repo.Reference,
							NewTestMerges = menu.Data.Values.Select(pr => new TestMergeParameters
							{
								Number = Int32.Parse(pr),
								Comment = "TgsLink",
							}).ToList(),
						};

						await menu.RespondAsync("Working.");
						await UpdateTestMergePanel((SocketGuildUser)menu.User, instance, menu.GuildId!.Value, menu.Message, true);
						await Task.Delay(2000);
						var instanceClient = await GetTgsInstance((SocketGuildUser)menu.User, instance);
						try
						{
							var resp = await instanceClient.Repository.Update(repoRequest, CancellationToken.None);
							if (resp.ActiveJob is { } job)
							{
								var checkCount = 0;
								do
								{
									checkCount++;
									await Task.Delay(1000);
									await menu.ModifyOriginalResponseAsync(props => props.Content = $"Checking (#{checkCount})");
									job = await UpdateJob((SocketGuildUser)menu.User, job, instance);
								}
								while (job.StoppedAt is null && checkCount < 10);
								if (job.StoppedAt is null)
								{
									await menu.ModifyOriginalResponseAsync(props => props.Content = "Probably failed to process test merges, job took too long to await!");
									return;
								}
								if (job.ExceptionDetails is { })
								{
									await menu.ModifyOriginalResponseAsync(props => props.Content = $"Failed to process test merges: `{job.ExceptionDetails}`");
									return;
								}
							}
							await menu.ModifyOriginalResponseAsync(props => props.Content = "Success");
						}
						catch (Exception e)
						{
							await menu.ModifyOriginalResponseAsync(props => props.Content = $"Failed: `{e.Message}`");
						}
						return;

					default:
						await menu.RespondAsync($"Unhandled TGS menu: `{command}`", ephemeral: true);
						return;
				}
			}

			await menu.RespondAsync("Unhandled menu!", ephemeral: true);
		}

		public async Task<JobResponse> UpdateJob(SocketGuildUser user, JobResponse job, long instanceId)
		{
			var instance = await GetTgsInstance(user, instanceId);
			return await instance.Jobs.GetId(new EntityId { Id = job.Id }, CancellationToken.None);
		}

		public override async Task OnButton(SocketMessageComponent button, string buttonId)
		{
			async Task<bool> AssetStoreInfo(PanelStore storeInfo)
			{
				if (storeInfo.InstanceId == -1)
				{
					await button.RespondAsync("Failed to get panel information?", ephemeral: true);
					return false;
				}
				if (storeInfo.User.Id != button.User.Id)
				{
					await button.RespondAsync("This isn't your panel!", ephemeral: true);
					return false;
				}

				return true;
			}

			if (buttonId.StartsWith("tgs/"))
			{
				var split = buttonId.Split("/");
				var command = split[1];
				long? instance = split.Length >= 3 ? Int64.Parse(split[2]) : null;
				var modifier = split.Length >= 4 ? split[3] : null;

				var storeInfo = new PanelStore { InstanceId = -1 };
				if (_userPanels.ContainsKey(button.User.Id))
					storeInfo = _userPanels[button.User.Id];

				var instanceActual = instance is { } ? await GetTgsInstance((SocketGuildUser)button.User, instance.Value) : null;
				switch (command)
				{
					case "refresh":
						if (!await AssetStoreInfo(storeInfo))
							return;
						await button.RespondAsync("Updating your panel...", ephemeral: true);
						await UpdateUserDaemonPanel(storeInfo);
						return;

					case "cancel":
						if (!await AssetStoreInfo(storeInfo))
							return;
						await button.RespondAsync("Canceling panel", ephemeral: true);
						await UpdateUserDaemonPanel(storeInfo, true);
						return;

					case "restart-type":
						if (!await AssetStoreInfo(storeInfo))
							return;
						switch (modifier)
						{
							case "nothing":
								await instanceActual!.DreamDaemon.Update(new DreamDaemonRequest { SoftRestart = false, SoftShutdown = false }, CancellationToken.None);
								await button.RespondAsync("World Reboot is now normal.");
								await UpdateUserDaemonPanel(storeInfo);
								return;

							case "restart":
								await instanceActual!.DreamDaemon.Update(new DreamDaemonRequest { SoftRestart = true, SoftShutdown = false }, CancellationToken.None);
								await button.RespondAsync("World Reboot will now restart.");
								await UpdateUserDaemonPanel(storeInfo);
								return;

							case "shutdown":
								await instanceActual!.DreamDaemon.Update(new DreamDaemonRequest { SoftRestart = false, SoftShutdown = true }, CancellationToken.None);
								await button.RespondAsync("World Reboot is now Graceful.");
								await UpdateUserDaemonPanel(storeInfo);
								return;

							default:
								await button.RespondAsync($"Unknown restart type: `{modifier}`", ephemeral: true);
								return;
						}

					case "set-watchdog":
						if (!await AssetStoreInfo(storeInfo))
							return;
						switch (modifier)
						{
							case "launch":
								await button.RespondAsync("Requesting a launch");
								await instanceActual!.DreamDaemon.Start(CancellationToken.None);
								await UpdateUserDaemonPanel(storeInfo);
								return;

							case "shutdown":
								await button.RespondAsync("Requesting a shutdown");
								await instanceActual!.DreamDaemon.Shutdown(CancellationToken.None);
								await UpdateUserDaemonPanel(storeInfo);
								return;

							default:
								await button.RespondAsync($"Unknown Watchdog State: `{modifier}`", ephemeral: true);
								return;
						}

					case "test-merge-menu":
						switch (modifier)
						{
							case "cancel":
								await UpdateTestMergePanel((SocketGuildUser)button.User, instance!.Value, button.GuildId!.Value, button.Message, true);
								await button.RespondAsync("Canceled Panel", ephemeral: true);
								return;

							default:
								await button.RespondAsync($"Unknown modifier: `{modifier}`", ephemeral: true);
								return;
						}


					default:
						await button.RespondAsync($"Unhandled TGS command: `{command}`", ephemeral: true);
						return;
				}
			}
			await button.RespondAsync("Unhandled button!", ephemeral: true);
		}

		[ModuleCommand("trigger a deployment")]
		public async Task StartDeployment(SocketSlashCommand command, long instanceId = 1)
		{
			var instance = await GetTgsInstance((SocketGuildUser)command.User, instanceId);
			await instance.DreamMaker.Compile(CancellationToken.None);
			await command.RespondAsync("Triggered a deployment, probably");
		}
	}
}
