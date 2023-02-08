using System.Buffers.Text;
using System.Text;

using Discord;
using Discord.WebSocket;

using Octokit;
using Octokit.Internal;

using Tgstation.Server.Api.Models;
using Tgstation.Server.Api.Models.Response;
using Tgstation.Server.Client;
using Tgstation.Server.Client.Components;

using ProductHeaderValue = System.Net.Http.Headers.ProductHeaderValue;

namespace Hoard2.Module.Builtin
{
	static class TgsHelpers
	{
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

	public class TgsLink : ModuleBase
	{
		Dictionary<SocketGuildUser, IServerClient> _tgsCache;

		Dictionary<ulong, Dictionary<string, TokenResponse>> _usernameTokenMap = new Dictionary<ulong, Dictionary<string, TokenResponse>>();

		public ServerClientFactory ClientFactory = new ServerClientFactory(new ProductHeaderValue("TgsLink"));
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
			if (password is null && usernamePassMap.ContainsKey(username)) return new Exception("Login information not saved");
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
		async Task<IServerClient> GetUserTgsClient(SocketGuildUser user)
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

		public async Task<PullRequest[]> GetPulls(SocketGuildUser user, long repository, bool onlyMarkedTM = true)
		{
			var client = GetUserGithubClient(user);
			var pulls = await client.PullRequest.GetAllForRepository(repository);
			if (onlyMarkedTM)
				pulls = pulls.Where(pull => pull.Labels.Any(label => label.Name.ToLower().Contains("test merge candidate"))).ToList();
			var sorted = pulls
				.OrderBy(pullInfo => pullInfo.Labels.Any(label => label.Name.ToLower().Contains("test merge candidate")))
				.ThenBy(pullInfo => pullInfo.Id);
			return sorted.ToArray();
		}

		public async Task<PullRequest[]> GetPulls(SocketUser user, string owner, string repository, bool onlyMarkedTM = true)
		{
			var client = GetUserGithubClient(user);
			var pulls = await client.PullRequest.GetAllForRepository(owner, repository);
			var sorted = pulls
				.OrderBy(pullInfo => pullInfo.Labels.Any(label => label.Name.ToLower().Contains("test merge candidate")))
				.ThenBy(pullInfo => pullInfo.Id);
			return sorted.ToArray();
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
				var openTestMergeMarked = await GetPulls(command.User, repository.RemoteRepositoryOwner!, repository.RemoteRepositoryName!);
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
	}
}
