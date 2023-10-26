using System.ComponentModel;
using Byond.TopicSender;
using Discord;
using Discord.WebSocket;
using Timer = System.Timers.Timer;

namespace Hoard2.Module.Builtin.SS13;

public class SS13Monitor : ModuleBase
{
    public SS13Monitor(string configPath) : base(configPath)
    {
        foreach (var guild in HoardMain.DiscordClient.Guilds)
            StartOrResetMonitor(guild.Id);
    }

    public override List<Type> GetConfigKnownTypes()
    {
        return new List<Type>()
        {
            typeof(ServerInformation),
            typeof(TimeSpan)
        };
    }

    public ServerInformation GetServerInfo(ulong guild)
    {
        return GuildConfig(guild).Get("server-info", new ServerInformation())!;
    }

    public void SetServerInfo(ulong guild, ServerInformation info)
    {
        GuildConfig(guild).Set("server-info", info);
    }

    private async Task UpdateServerFunc(ulong guild)
    {
        var serverInfo = GetServerInfo(guild);
        if (!serverInfo.IsValid)
            return;

        var fiveSecondTimeSpan = TimeSpan.FromSeconds(5);
        var client = new TopicClient(new SocketParameters
        {
            ConnectTimeout = fiveSecondTimeSpan,
            DisconnectTimeout = fiveSecondTimeSpan,
            ReceiveTimeout = fiveSecondTimeSpan,
            SendTimeout = fiveSecondTimeSpan
        });

        var sendTopicTask = client.SendTopic(
            serverInfo.Address, $"status&key={serverInfo.CommKey}",
            serverInfo.Port, CancellationToken.None);
        await sendTopicTask.WaitAsync(CancellationToken.None);
        await UpdateMonitorMessage(guild, sendTopicTask.IsCompletedSuccessfully ? sendTopicTask.Result : null,
            serverInfo);
    }

    private Dictionary<ulong, Timer> _monitors = new();

    private void StartOrResetMonitor(ulong guild)
    {
        var serverInfo = GetServerInfo(guild);
        if (!serverInfo.IsValid)
            return;

        if (_monitors.TryGetValue(guild, out var timer))
        {
            timer.Stop();
            timer.Dispose();
        }

        _monitors[guild] = timer = new Timer();
        timer.AutoReset = true;
        timer.Interval = serverInfo.UpdatePeriod.TotalMilliseconds;
        timer.Elapsed += (_, _) => UpdateServerFunc(guild).Wait();
        timer.Start();
    }

    public async Task<IMessageChannel> GetMonitorChannel(ulong guild)
    {
        return (IMessageChannel)
            await HoardMain.DiscordClient.GetChannelAsync(GuildConfig(guild).Get<ulong>("mon-channel"));
    }

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

    private async Task UpdateMonitorMessage(ulong guild, TopicResponse? statusResponse, ServerInformation info)
    {
        var message = await GetMonitorMessage(guild);
        var builder = new EmbedBuilder()
            .WithTitle($"Status - {info.Name}")
            .WithTimestamp(DateTimeOffset.UtcNow);
        if (statusResponse is null)
        {
            await message.ModifyAsync(props =>
            {
                props.Content = string.Empty;
                props.Embed = builder
                    .WithDescription("Server Offline")
                    .WithColor(Color.DarkRed)
                    .Build();
            });
        }
        else
        {
            var stringResponse = statusResponse.StringData;
            if (stringResponse is null || stringResponse.ToLower().Equals("rate limited."))
                return;
            var strings = statusResponse.StringData!.Split('&',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var jsonDict = new Dictionary<string, string?>();
            foreach (var entry in strings)
            {
                if (!entry.Contains('=')) continue;
                var values = entry.Split('=');
                jsonDict[values[0]] = values[1];
            }

            var durationString = jsonDict.TryGetValue("round_duration", out var durationSeconds) &&
                                 durationSeconds is not null
                ? TimeSpan.FromSeconds(double.Parse(durationSeconds)).ToString("hh\\:mm\\:ss")
                : "!NULL!";
            await message.ModifyAsync(props =>
            {
                props.Content = string.Empty;
                props.Embed = builder
                    .WithDescription($"" +
                                     $"Players:      `{jsonDict["players"] ?? "!NULL!"}`\n" +
                                     $"Round Length: `{durationString}`\n" +
                                     $"Round:        `{(jsonDict.TryGetValue("round_id", out var roundId) ? roundId : "!NULL!")}`\n" +
                                     $"TIDI:         `{jsonDict["time_dilation_current"] ?? "!NULL!"}% ({jsonDict["time_dilation_avg"] ?? "!NULL!"}%)`\n" +
                                     $"Next update <t:{DateTimeOffset.UtcNow.Add(info.UpdatePeriod).ToUnixTimeSeconds() + 2}:R>\n")
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
            StartOrResetMonitor(command.GuildId.Value);
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
            StartOrResetMonitor(command.GuildId.Value);
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
            StartOrResetMonitor(command.GuildId.Value);
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
            StartOrResetMonitor(command.GuildId.Value);
    }

    [ModuleCommand(GuildPermission.Administrator)]
    [CommandGuildOnly]
    public async Task ForceUpdate(SocketSlashCommand command)
    {
        StartOrResetMonitor(command.GuildId!.Value);
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

        GuildConfig(command.GuildId!.Value).Set("mon-channel", channel.Id);
        await command.RespondAsync("Set the channel");
        StartOrResetMonitor(command.GuildId.Value);
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
