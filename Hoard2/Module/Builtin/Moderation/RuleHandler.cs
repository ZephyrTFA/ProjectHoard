using Discord;
using Discord.WebSocket;

namespace Hoard2.Module.Builtin.Moderation;

public class RuleData
{
    public ulong GuildId { get; set; } = 0;
    public string[] Rules { get; set; } = Array.Empty<string>();
    public ulong RuleChannel { get; set; } = 0;
    public ulong[] RuleMessages { get; set; } = Array.Empty<ulong>();
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
        Array.Clear(data.RuleMessages);
    }

    private static async Task SendRules(SocketGuild guild, RuleData data, ulong? channelOverride = null)
    {
        if (await HoardMain.DiscordClient.GetChannelAsync(channelOverride ?? data.RuleChannel) is not IMessageChannel channel) return;
        var ruleMessages = data.RuleMessages;
        Array.Resize(ref ruleMessages, data.Rules.Length);
        for (var ruleIdx = 0; ruleIdx < data.Rules.Length; ruleIdx++)
        {
            var messageId =  (await channel.SendMessageAsync(data.Rules[ruleIdx])).Id;
            if(channelOverride is not null) continue; // if we are passed an override channel, dont update locations
            data.RuleMessages[ruleIdx] = messageId;
        }
    }

    [ModuleCommand]
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

    [ModuleCommand]
    [CommandGuildOnly]
    public async Task DeleteRuleMessages(SocketSlashCommand command)
    {
        var guild = HoardMain.DiscordClient.GetGuild(command.GuildId!.Value)!;
        var ruleData = GetRuleData(guild.Id);
        await DeleteRules(guild, ruleData);
        await command.RespondAsync("Deleted the rule messages.");
    }

    [ModuleCommand]
    [CommandGuildOnly]
    public async Task ShowRules(SocketSlashCommand command)
    {
        var guild = HoardMain.DiscordClient.GetGuild(command.GuildId!.Value)!;
        var ruleData = GetRuleData(guild.Id);
        await SendRules(guild, ruleData, command.ChannelId!.Value);
        await command.RespondAsync("Sent.");
    }

    [ModuleCommand]
    [CommandGuildOnly]
    public async Task SetRule(SocketSlashCommand command, int ruleNumber, string ruleText)
    {
        var guild = HoardMain.DiscordClient.GetGuild(command.GuildId!.Value)!;
        var ruleData = GetRuleData(guild.Id);
        var rules = ruleData.Rules;

        if (ruleNumber > ruleData.Rules.Length)
            Array.Resize(ref rules, ruleNumber + 1);

        rules[ruleNumber] = ruleText;
        SetRuleData(guild.Id, ruleData);
        await command.RespondAsync("Updated.");
    }

    public async Task DropRule(SocketSlashCommand command, int ruleNumber)
    {
        var guild = HoardMain.DiscordClient.GetGuild(command.GuildId!.Value)!;
        var ruleData = GetRuleData(guild.Id);
        var rules = ruleData.Rules;
        Array.ConstrainedCopy(rules, ruleNumber + 1, rules, ruleNumber, rules.Length - ruleNumber + 1);
        SetRuleData(guild.Id, ruleData);
        await command.RespondAsync("Dropped.");
    }
}
