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

		public long DefaultInstance { get; set; } = 0;
	}

	public class TGSLink : ModuleBase
	{
		public override List<Type> GetConfigKnownTypes() => new List<Type>
		{
			typeof(TgsServerInformation),
		};

		public TGSLink(string configPath) : base(configPath) { }

		public TgsServerInformation GetServerInformation(ulong guild) => GuildConfig(guild).Get("server-info", new TgsServerInformation())!;

		public void SetServerInformation(ulong guild, TgsServerInformation info) => GuildConfig(guild).Set("server-info", info);

		Dictionary<ulong, TokenResponse> _userTokenMap = new Dictionary<ulong, TokenResponse>();
		Dictionary<ulong, IServerClient> _userClientMap = new Dictionary<ulong, IServerClient>();
		ServerClientFactory _userTgsClientFactory = new ServerClientFactory(new ProductHeaderValue("ProjectHoard-TgsLink"));

		IServerClient? GetUserTgsClient(Uri server, IUser user)
		{
			if (_userClientMap.TryGetValue(user.Id, out var existingClient))
				if (existingClient.Token.ExpiresAt.CompareTo(DateTimeOffset.Now) > 0)
					return existingClient;
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
		
		[ModuleCommand]
		[CommandGuildOnly]
		public async Task Login(SocketSlashCommand command, string username, string password)
		{
			await command.DeferAsync(ephemeral: true);
			var serverInfo = GetServerInformation(command.GuildId!.Value);
			if (await DoUserLogin(serverInfo.ServerUri, command.User, username, password))
				await command.SendOrModifyOriginalResponse("Logged in.");
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
			if (GetUserTgsClient(serverInfo.ServerUri, command.User) is not { } client)
			{
				await command.RespondAsync("You must login first.");
				return;
			}

			await command.DeferAsync();
			if(instanceId is -1)
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

			var responseBuilder = new StringBuilder("Active Test Merges:\n```\n");
			foreach (var testMergeInfo in testMerges)
			{
				responseBuilder.AppendLine($"#{testMergeInfo.Number} - {testMergeInfo.TitleAtMerge}");
				responseBuilder.AppendLine($"\t- @{testMergeInfo.TargetCommitSha ?? "HEAD"}");
			}
			responseBuilder.AppendLine("```");

			await command.SendOrModifyOriginalResponse(responseBuilder.ToString());
		}
	}
}
