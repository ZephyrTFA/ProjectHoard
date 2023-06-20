using System.ComponentModel;

using Byond.TopicSender;

using Discord;
using Discord.WebSocket;

using Newtonsoft.Json;

namespace Hoard2.Module.Builtin.Afterglow
{
	public class SS13Monitor : ModuleBase
	{
		Dictionary<ulong, (Task, CancellationTokenSource)> _monitorThreads = new Dictionary<ulong, (Task, CancellationTokenSource)>();

		public SS13Monitor(string configPath) : base(configPath)
		{
			foreach (var guild in HoardMain.DiscordClient.Guilds)
				StartMonitorTask(guild.Id);
		}

		public override List<Type> GetConfigKnownTypes() => new List<Type>
		{
			typeof(ServerInformation),
			typeof(TimeSpan),
		};

		public ServerInformation GetServerInfo(ulong guild) => GuildConfig(guild).Get("server-info", new ServerInformation())!;

		public void SetServerInfo(ulong guild, ServerInformation info) => GuildConfig(guild).Set("server-info", info);

		void StartMonitorTask(ulong guild)
		{
			async Task DoMonitorThread(CancellationToken token)
			{
				while (true)
				{
					var serverInfo = GetServerInfo(guild);
					if (!serverInfo.IsValid)
						return;

					var client = new TopicClient(new SocketParameters());
					try
					{
						var resp = await client.SendTopic(serverInfo.Address, $"?status&key={serverInfo.CommKey}", serverInfo.Port, token);
						await UpdateMonitorMessage(guild, resp, serverInfo);
					}
					catch
					{
						// probably offline
						await UpdateMonitorMessage(guild, null, serverInfo);
					}

					await Task.Delay(serverInfo.UpdatePeriod, token);
				}
			}
			if (_monitorThreads.TryGetValue(guild, out var thread))
				thread.Item2.Cancel();
			var source = new CancellationTokenSource();
			_monitorThreads[guild] = (DoMonitorThread(source.Token), source);
		}

		public async Task<IMessageChannel> GetMonitorChannel(ulong guild) => (IMessageChannel)
			await HoardMain.DiscordClient.GetChannelAsync(GuildConfig(guild).Get<ulong>("mon-channel"));

		public async Task<IUserMessage> GetMonitorMessage(ulong guild)
		{
			var channel = await GetMonitorChannel(guild);
			var config = GuildConfig(guild);
			var message = config.Get<ulong>("mon-message");
			IUserMessage? messageActual = null;
			if (message is not 0)
				messageActual = await channel.GetMessageAsync(message) as IUserMessage;
			messageActual ??= await channel.SendMessageAsync("caching context");
			if (message != messageActual.Id)
				config.Set("mon-message", messageActual.Id);
			return messageActual;
		}

		async Task UpdateMonitorMessage(ulong guild, TopicResponse? statusResponse, ServerInformation info)
		{
			var message = await GetMonitorMessage(guild);
			var builder = new EmbedBuilder()
				.WithTitle($"Status - {info.Name}")
				.WithFooter($"Next update in <t:{DateTimeOffset.UtcNow.Add(info.UpdatePeriod).ToUnixTimeSeconds()}:R>")
				.WithTimestamp(DateTimeOffset.UtcNow);
			if (statusResponse is null)
			{
				await message.ModifyAsync(props =>
				{
					props.Content = String.Empty;
					props.Embed = builder
						.WithDescription("Server Offline")
						.WithColor(Color.DarkRed)
						.Build();
				});
			}
			else
			{
				var jsonDict = (Dictionary<string, string>)JsonConvert.DeserializeObject(statusResponse.StringData!, typeof(Dictionary<string, string>))!;
				await message.ModifyAsync(props =>
				{
					props.Content = String.Empty;
					props.Embed = builder
						.WithDescription($"" +
							$"Players:      `{jsonDict["players"]}`\n" +
							$"Round Length: `{jsonDict["round_duration"]}`\n" +
							$"Round:        `{jsonDict["round_id"]}`" +
							$"TIDI:         `{jsonDict["time_dilation_current"]}% ({jsonDict["time_dilation_avg"]}%)`" +
							$"[Join](byond://{info.Address}:{info.Port}/)")
						.Build();
				});
			}
		}

		[ModuleCommand(GuildPermission.Administrator)]
		[CommandGuildOnly]
		public async Task GetServerInformation(SocketSlashCommand command)
		{
			var serverInfo = GetServerInfo(command.GuildId!.Value);
			await command.RespondAsync(embed:
				new EmbedBuilder()
					.WithTitle("Server Information")
					.WithDescription("```\n" +
						$"Name:    {serverInfo.Name}\n" +
						$"Address: {serverInfo.Address}\n" +
						"CommKey: [REDACTED]\n" +
						$"Port:    {serverInfo.Port}\n" +
						$"UpdateP: {serverInfo.UpdatePeriod.TotalSeconds}s\n" +
						"```\n")
					.Build()
			);
		}

		[ModuleCommand(GuildPermission.Administrator)]
		[CommandGuildOnly]
		public async Task SetServerName(SocketSlashCommand command, string name)
		{
			var serverInfo = GetServerInfo(command.GuildId!.Value);
			serverInfo.Name = name;
			SetServerInfo(command.GuildId.Value, serverInfo);
			await command.RespondAsync("Updated the name.");
		}

		[ModuleCommand(GuildPermission.Administrator)]
		[CommandGuildOnly]
		public async Task SetServerAddress(SocketSlashCommand command, string address)
		{
			var serverInfo = GetServerInfo(command.GuildId!.Value);
			serverInfo.Address = address;
			SetServerInfo(command.GuildId.Value, serverInfo);
			await command.RespondAsync("Updated the address.");
			if (serverInfo.IsValid)
				StartMonitorTask(command.GuildId.Value);
		}

		[ModuleCommand(GuildPermission.Administrator)]
		[CommandGuildOnly]
		public async Task SetServerPort(SocketSlashCommand command, long port)
		{
			var serverInfo = GetServerInfo(command.GuildId!.Value);
			serverInfo.Port = (ushort)port;
			SetServerInfo(command.GuildId.Value, serverInfo);
			await command.RespondAsync("Updated the port.");
			if (serverInfo.IsValid)
				StartMonitorTask(command.GuildId.Value);
		}

		[ModuleCommand(GuildPermission.Administrator)]
		[CommandGuildOnly]
		public async Task SetUpdatePeriod(SocketSlashCommand command, [Description("minimum 10")] long seconds)
		{
			if (seconds < 10) seconds = 10;
			var serverInfo = GetServerInfo(command.GuildId!.Value);
			serverInfo.UpdatePeriod = TimeSpan.FromSeconds(seconds);
			SetServerInfo(command.GuildId.Value, serverInfo);
			await command.RespondAsync("Updated the update period.");
			if (serverInfo.IsValid)
				StartMonitorTask(command.GuildId.Value);
		}

		[ModuleCommand(GuildPermission.Administrator)]
		[CommandGuildOnly]
		public async Task SetCommKey(SocketSlashCommand command, string key)
		{
			var serverInfo = GetServerInfo(command.GuildId!.Value);
			serverInfo.CommKey = key;
			SetServerInfo(command.GuildId.Value, serverInfo);
			await command.RespondAsync("Updated the comm key.", ephemeral: true);
			if (serverInfo.IsValid)
				StartMonitorTask(command.GuildId.Value);
		}

		[ModuleCommand(GuildPermission.Administrator)]
		[CommandGuildOnly]
		public async Task ForceUpdate(SocketSlashCommand command)
		{
			StartMonitorTask(command.GuildId!.Value);
			await command.RespondAsync("Forced an update");
		}

		[ModuleCommand(GuildPermission.Administrator)]
		[CommandGuildOnly]
		public async Task SetMonitorChannel(SocketSlashCommand command, IChannel channel)
		{
			if (channel is not IMessageChannel)
			{
				await command.RespondAsync("Must be a message channel!");
				return;
			}

			var config = GuildConfig(command.GuildId!.Value);
			var current = config.Get<ulong>("mon-channel");
			var curMessage = config.Get<ulong>("mon-message");
			if (current is not 0 && curMessage is not 0)
				await ((IMessageChannel)await HoardMain.DiscordClient.GetChannelAsync(current)).DeleteMessageAsync(current);
			GuildConfig(command.GuildId!.Value).Set("mon-channel", channel.Id);
			await command.RespondAsync("Set the channel");
			StartMonitorTask(command.GuildId.Value);
		}

		public class ServerInformation
		{
			public string Name { get; set; } = "SS13";

			public string Address { get; set; } = "localhost";

			public string CommKey { get; set; } = "NoKeySet";

			public ushort Port { get; set; }

			public TimeSpan UpdatePeriod { get; set; } = TimeSpan.FromSeconds(10);

			public bool IsValid => Port is not 0;
		}
	}
}
