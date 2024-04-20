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

    private static async Task<bool> DeleteRules(SocketGuild guild, RuleData data)
    {
        if (data.RuleChannel == 0)
            return false;
        if (await HoardMain.DiscordClient.GetChannelAsync(data.RuleChannel) is not IMessageChannel channel) return false;
        var failed = false;
        foreach (var ruleMessage in data.RuleMessages)
            try
            {
                await channel.DeleteMessageAsync(ruleMessage);
            }
            catch
            {
                failed = true;
            }
        data.RuleMessages.Clear();
        return failed;
    }

    private static async Task SendRules(SocketGuild guild, RuleData data, ulong? channelOverride = null, bool showRuleNums = false)
    {
        var channelUse = channelOverride ?? data.RuleChannel;
        if (channelUse == 0)
            return;
        if (await HoardMain.DiscordClient.GetChannelAsync(channelOverride ?? data.RuleChannel) is not IMessageChannel
            channel) return;
        data.RuleMessages.Clear();
        data.RuleMessages.Capacity = data.Rules.Count;
        var idx = 0;
        foreach (var ruleMessage in data.Rules.Select(rule => rule.ToString()))
        {
            var ruleBuilder = new StringBuilder(ruleMessage);
            if (showRuleNums) ruleBuilder.Insert(0, $"R-`{idx++}` | ");
            var messageId = (await channel.SendMessageAsync(ruleBuilder.ToString())).Id;
            if (channelOverride is not null) continue; // if we are passed an override channel, dont update locations
            data.RuleMessages.Add(messageId);
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
        await command.RespondAsync("Deleting...");
        var guild = HoardMain.DiscordClient.GetGuild(command.GuildId!.Value)!;
        var ruleData = GetRuleData(guild.Id);
        await DeleteRules(guild, ruleData);
        SetRuleData(guild.Id, ruleData);
    }

    [ModuleCommand(GuildPermission.Administrator)]
    [CommandGuildOnly]
    public async Task ShowRules(SocketSlashCommand command)
    {
        await command.RespondAsync("Sending...");

        await command.Channel.SendMessageAsync("`R-##` only shows here. Will not be shown in a actual rule messages.");
        var guild = HoardMain.DiscordClient.GetGuild(command.GuildId!.Value)!;
        var ruleData = GetRuleData(guild.Id);
        await SendRules(guild, ruleData, command.ChannelId!.Value, true);
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
