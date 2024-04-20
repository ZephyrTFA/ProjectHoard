using System.Text;
using Discord;
using Discord.WebSocket;

namespace Hoard2.Module.Builtin.Moderation;

public class RuleData
{
    public List<Rule> Rules { get; set; } = new();
    public ulong RuleChannel { get; set; }
    public List<ulong> RuleMessages { get; set; } = new();
}

public class Rule
{
    public string RuleText = string.Empty;
    public string RuleHeader = string.Empty;

    public override string ToString()
    {
        return $"{RuleHeader}\n\n{RuleText}";
    }
}

public class RuleHandler : ModuleBase
{
    public RuleHandler(string configPath) : base(configPath)
    {
    }

    public override List<Type> GetConfigKnownTypes()
    {
        return new List<Type>
        {
            typeof(RuleData), typeof(List<ulong>), typeof(List<Rule>), typeof(Rule),
        };
    }

    public RuleData GetRuleData(ulong guild) => GuildConfig(guild).Get("rule-data", new RuleData())!;
    public void SetRuleData(ulong guild, RuleData data) => GuildConfig(guild).Set("rule-data", data);

    private static async Task DeleteRules(SocketGuild guild, RuleData data)
    {
        if (data.RuleChannel == 0)
            return;
        if (await HoardMain.DiscordClient.GetChannelAsync(data.RuleChannel) is not IMessageChannel channel) return;
        foreach (var ruleMessage in data.RuleMessages) await channel.DeleteMessageAsync(ruleMessage);
        data.RuleMessages.Clear();
    }

    private static async Task SendRules(SocketGuild guild, RuleData data, ulong? channelOverride = null)
    {
        var channelUse = channelOverride ?? data.RuleChannel;
        if (channelUse == 0)
            return;
        if (await HoardMain.DiscordClient.GetChannelAsync(channelOverride ?? data.RuleChannel) is not IMessageChannel
            channel) return;
        data.RuleMessages.Capacity = data.Rules.Count;
        for (var ruleIdx = 0; ruleIdx < data.Rules.Count; ruleIdx++)
        {
            var messageId = (await channel.SendMessageAsync(data.Rules[ruleIdx].ToString())).Id;
            if (channelOverride is not null) continue; // if we are passed an override channel, dont update locations
            data.RuleMessages[ruleIdx] = messageId;
        }
    }

    [ModuleCommand(GuildPermission.Administrator)]
    [CommandGuildOnly]
    public async Task UpdateRules(SocketSlashCommand command)
    {
        await command.RespondAsync("Updating...");
        var guild = HoardMain.DiscordClient.GetGuild(command.GuildId!.Value)!;
        var ruleData = GetRuleData(guild.Id);
        await DeleteRules(guild, ruleData);
        await SendRules(guild, ruleData);
        SetRuleData(guild.Id, ruleData);
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
        await command.RespondAsync("Sending...");
        var guild = HoardMain.DiscordClient.GetGuild(command.GuildId!.Value)!;
        var ruleData = GetRuleData(guild.Id);
        await SendRules(guild, ruleData, command.ChannelId!.Value);
    }

    [ModuleCommand(GuildPermission.Administrator)]
    [CommandGuildOnly]
    public async Task SetRule(SocketSlashCommand command, int ruleNumber, string ruleHeader, string ruleText,
        bool overrideRule = true)
    {
        var guild = HoardMain.DiscordClient.GetGuild(command.GuildId!.Value)!;
        var ruleData = GetRuleData(guild.Id);

        if (ruleNumber > ruleData.Rules.Count)
            ruleData.Rules.Capacity = ruleNumber + 1;
        if (overrideRule && ruleNumber < ruleData.Rules.Count)
            ruleData.Rules.RemoveAt(ruleNumber);

        ruleData.Rules.Insert(ruleNumber, new Rule
        {
            RuleText = ruleText,
            RuleHeader = ruleHeader,
        });
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

    [ModuleCommand(GuildPermission.Administrator)]
    [CommandGuildOnly]
    public async Task SetRuleChannel(SocketSlashCommand command, IMessageChannel channel)
    {
        await command.RespondAsync("Updating.");
        var guild = HoardMain.DiscordClient.GetGuild(command.GuildId!.Value)!;
        var ruleData = GetRuleData(command.GuildId!.Value);
        await DeleteRules(guild, ruleData);
        ruleData.RuleChannel = channel.Id;
        await SendRules(guild, ruleData);
        SetRuleData(command.GuildId!.Value, ruleData);
    }
}
