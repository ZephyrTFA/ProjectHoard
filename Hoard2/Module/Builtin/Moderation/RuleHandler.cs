using Discord;
using Discord.WebSocket;

namespace Hoard2.Module.Builtin.Moderation;

public class RuleData
{
    public ulong GuildId { get; set; } = 0;
    public List<string> Rules { get; set; } = new();
    public ulong RuleChannel { get; set; } = 0;
    public List<ulong> RuleMessages { get; set; } = new();
}

public class RuleHandler : ModuleBase
{
    public RuleHandler(string configPath) : base(configPath)
    {
    }

    public RuleData GetRuleData(ulong guild) => GuildConfig(guild).Get("rule-data", new RuleData())!;
    public void SetRuleData(ulong guild, RuleData data) => GuildConfig(guild).Set("rule-data", data);

    private static async Task DeleteRules(SocketGuild guild, RuleData data)
    {
        if (await HoardMain.DiscordClient.GetChannelAsync(data.RuleChannel) is not IMessageChannel channel) return;
        foreach (var ruleMessage in data.RuleMessages) await channel.DeleteMessageAsync(ruleMessage);
        data.RuleMessages.Clear();
    }

    private static async Task SendRules(SocketGuild guild, RuleData data, ulong? channelOverride = null)
    {
        if (await HoardMain.DiscordClient.GetChannelAsync(channelOverride ?? data.RuleChannel) is not IMessageChannel
            channel) return;
        data.RuleMessages.Capacity = data.Rules.Count;
        for (var ruleIdx = 0; ruleIdx < data.Rules.Count; ruleIdx++)
        {
            var messageId = (await channel.SendMessageAsync(data.Rules[ruleIdx])).Id;
            if (channelOverride is not null) continue; // if we are passed an override channel, dont update locations
            data.RuleMessages[ruleIdx] = messageId;
        }
    }

    [ModuleCommand(GuildPermission.Administrator)]
    [CommandGuildOnly]
    public async Task UpdateRules(SocketSlashCommand command)
    {
        var guild = HoardMain.DiscordClient.GetGuild(command.GuildId!.Value)!;
        var ruleData = GetRuleData(guild.Id);
        await DeleteRules(guild, ruleData);
        await SendRules(guild, ruleData);
        SetRuleData(guild.Id, ruleData);
        await command.RespondAsync("Updated the rules.", ephemeral: true);
    }

    [ModuleCommand(GuildPermission.Administrator)]
    [CommandGuildOnly]
    public async Task DeleteRuleMessages(SocketSlashCommand command)
    {
        var guild = HoardMain.DiscordClient.GetGuild(command.GuildId!.Value)!;
        var ruleData = GetRuleData(guild.Id);
        await DeleteRules(guild, ruleData);
        await command.RespondAsync("Deleted the rule messages.");
    }

    [ModuleCommand(GuildPermission.Administrator)]
    [CommandGuildOnly]
    public async Task ShowRules(SocketSlashCommand command)
    {
        var guild = HoardMain.DiscordClient.GetGuild(command.GuildId!.Value)!;
        var ruleData = GetRuleData(guild.Id);
        await SendRules(guild, ruleData, command.ChannelId!.Value);
        await command.RespondAsync("Sent.");
    }

    [ModuleCommand(GuildPermission.Administrator)]
    [CommandGuildOnly]
    public async Task SetRule(SocketSlashCommand command, int ruleNumber, string ruleText)
    {
        var guild = HoardMain.DiscordClient.GetGuild(command.GuildId!.Value)!;
        var ruleData = GetRuleData(guild.Id);

        if (ruleNumber > ruleData.Rules.Count)
            ruleData.Rules.Capacity = ruleNumber + 1;

        ruleData.Rules[ruleNumber] = ruleText;
        SetRuleData(guild.Id, ruleData);
        await command.RespondAsync("Updated.");
    }

    [ModuleCommand(GuildPermission.Administrator)]
    [CommandGuildOnly]
    public async Task DropRule(SocketSlashCommand command, int ruleNumber)
    {
        var guild = HoardMain.DiscordClient.GetGuild(command.GuildId!.Value)!;
        var ruleData = GetRuleData(guild.Id);
        ruleData.Rules.RemoveAt(ruleNumber);
        SetRuleData(guild.Id, ruleData);
        await command.RespondAsync("Dropped.");
    }
}
