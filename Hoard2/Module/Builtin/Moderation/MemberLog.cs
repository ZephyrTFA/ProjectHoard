﻿using System.ComponentModel;
using System.Text;
using Discord;
using Discord.Rest;
using Discord.WebSocket;

namespace Hoard2.Module.Builtin.Moderation;

public class MemberLog : ModuleBase
{
    public const string ChannelJoin = "channel-join";
    public const string ChannelLeave = "channel-leave";
    public const string ChannelBan = "channel-ban";
    public const string ChannelUnban = "channel-unban";
    public const string ChannelUpdate = "channel-update";

    public MemberLog(string configPath) : base(configPath)
    {
    }

    [ModuleCommand(GuildPermission.Administrator)]
    [Description("Update the target channel for member joins.")]
    public async Task SetJoinChannel(SocketSlashCommand command, IChannel channel)
    {
        GuildConfig(command.GuildId!.Value).Set(ChannelJoin, channel.Id);
        await command.RespondAsync($"Updated the target log channel to <#{channel.Id}>");
    }

    [ModuleCommand(GuildPermission.Administrator)]
    [Description("Update the target channel for member leaves.")]
    public async Task SetLeaveChannel(SocketSlashCommand command, IChannel channel)
    {
        GuildConfig(command.GuildId!.Value).Set(ChannelLeave, channel.Id);
        await command.RespondAsync($"Updated the target log channel to <#{channel.Id}>");
    }

    [ModuleCommand(GuildPermission.Administrator)]
    [Description("Update the target channel for member unbans.")]
    public async Task SetBanChannel(SocketSlashCommand command, IChannel channel)
    {
        GuildConfig(command.GuildId!.Value).Set(ChannelBan, channel.Id);
        await command.RespondAsync($"Updated the target log channel to <#{channel.Id}>");
    }

    [ModuleCommand(GuildPermission.Administrator)]
    [Description("Update the target channel for member updates.")]
    public async Task SetUpdateChannel(SocketSlashCommand command, IChannel channel)
    {
        GuildConfig(command.GuildId!.Value).Set(ChannelUpdate, channel.Id);
        await command.RespondAsync($"Updated the target log channel to <#{channel.Id}>");
    }

    [ModuleCommand(GuildPermission.Administrator)]
    [Description("Update the target channel for member bans.")]
    public async Task SetUnbanChannel(SocketSlashCommand command, IChannel channel)
    {
        GuildConfig(command.GuildId!.Value).Set(ChannelUnban, channel.Id);
        await command.RespondAsync($"Updated the target log channel to <#{channel.Id}>");
    }

    private async Task<IMessageChannel?> GetChannel(ulong guild, string key)
    {
        if (!GuildConfig(guild).TryGet<ulong>(key, out var channelId))
            return null;
        if (channelId == 0)
            return null;
        return await HoardMain.DiscordClient.GetChannelAsync(channelId) as IMessageChannel;
    }

    [ModuleCommand(GuildPermission.ManageNicknames)]
    [CommandGuildOnly]
    [Description("Check the nickname history for the given user.")]
    public static async Task CheckNicknameHistory(SocketSlashCommand command, IGuildUser user)
    {
        if (!ModuleHelper.TryGetModule(out UserDataHelper? userData))
        {
            await command.RespondAsync("Failed to load UserDataHelper", ephemeral: true);
            return;
        }

        var nicks = userData.GetGuildNicknameHistory(user).Take(20);
        var response = new StringBuilder($"Previous nicknames (max 20) for {user.Mention}:\n");
        foreach (var nick in nicks)
            response.AppendLine($"- `{nick}`");
        await command.RespondAsync(response.ToString(), allowedMentions: AllowedMentions.None);
    }

    [ModuleCommand(GuildPermission.Administrator)]
    [Description("Check the current target channel.")]
    public async Task CheckMemberLogChannel(SocketSlashCommand command)
    {
        var joinChannel = await GetChannel(command.GuildId!.Value, ChannelJoin);
        var leaveChannel = await GetChannel(command.GuildId!.Value, ChannelLeave);
        var banChannel = await GetChannel(command.GuildId!.Value, ChannelBan);
        var unbanChannel = await GetChannel(command.GuildId!.Value, ChannelUnban);
        var updateChannel = await GetChannel(command.GuildId!.Value, ChannelUpdate);

        var joinText = joinChannel is not null ? $"<#{joinChannel.Id}>" : "Not Set";
        var leaveText = leaveChannel is not null ? $"<#{leaveChannel.Id}>" : "Not Set";
        var banText = banChannel is not null ? $"<#{banChannel.Id}>" : "Not Set";
        var unbanText = unbanChannel is not null ? $"<#{unbanChannel.Id}>" : "Not Set";
        var updateText = updateChannel is not null ? $"<#{updateChannel.Id}>" : "Not Set";

        await command.RespondAsync("Channel Map", embed:
            new EmbedBuilder()
                .WithCurrentTimestamp()
                .WithTitle("Channel Map")
                .WithDescription(
                    $"**Join:**   - {joinText}\n" +
                    $"**Leave:**  - {leaveText}\n" +
                    $"**Ban:**    - {banText}\n" +
                    $"**Unban:**  - {unbanText}\n" +
                    $"** Update:** - {updateText}\n"
                )
                .Build());
    }

    public override async Task DiscordClientOnUserJoined(SocketGuildUser socketGuildUser)
    {
        if (GuildConfig(socketGuildUser.Guild.Id).Get<ulong?>(ChannelJoin) is not { } channelId)
            return;
        var channel = await HoardMain.DiscordClient.GetChannelAsync(channelId);
        if (channel is not IMessageChannel messageChannel)
        {
            HoardMain.Logger.LogWarning("Could not fetch channel!");
            return;
        }

        await messageChannel.SendMessageAsync(embed: new EmbedBuilder()
            .WithAuthor(socketGuildUser)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .WithColor(Color.Blue)
            .WithTitle($"{socketGuildUser.Username} has joined the Guild.")
            .WithDescription($"<@!{socketGuildUser.Id}>")
            .WithImageUrl(socketGuildUser.GetDisplayAvatarUrl())
            .WithFields(new EmbedFieldBuilder().WithName("Total Members").WithValue(socketGuildUser.Guild.MemberCount))
            .WithFields(new EmbedFieldBuilder().WithName("Creation Date").WithValue(socketGuildUser.CreatedAt))
            .WithFields(new EmbedFieldBuilder().WithName("UID").WithValue($"{socketGuildUser.Id}"))
            .Build());
    }

    public override async Task DiscordClientOnUserBanned(SocketUser user, SocketGuild guild)
    {
        if (GuildConfig(guild.Id).Get<ulong?>(ChannelBan) is not { } channelId)
            return;
        var channel = await HoardMain.DiscordClient.GetChannelAsync(channelId);
        if (channel is not IMessageChannel messageChannel)
        {
            HoardMain.Logger.LogWarning("Could not fetch channel!");
            return;
        }

        var auditLogEntries = await guild.GetAuditLogsAsync(20, actionType: ActionType.Ban).FlattenAsync();
        var entry = auditLogEntries.FirstOrDefault(logEntry =>
        {
            var data = (BanAuditLogData)logEntry.Data;
            if (data.Target.Id == user.Id)
                return true;
            return false;
        });
        var moderator = entry is not null ? $"<@!{entry.User.Id}>" : "Unknown Moderator";
        var ban = await guild.GetBanAsync(user);
        await messageChannel.SendMessageAsync(embed: new EmbedBuilder()
            .WithAuthor(user)
            .WithCurrentTimestamp()
            .WithColor(Color.DarkRed)
            .WithTitle($"{user.Username} was banned.")
            .WithDescription($"<@!{user.Id}>")
            .WithFields(
                new EmbedFieldBuilder().WithName("Moderator").WithValue(moderator),
                new EmbedFieldBuilder().WithName("UID").WithValue($"{user.Id}"),
                new EmbedFieldBuilder().WithName("Ban Reason")
                    .WithValue(string.IsNullOrWhiteSpace(ban.Reason) ? "No ban reason supplied" : ban.Reason))
            .WithImageUrl(user.GetAvatarUrl())
            .Build());
    }

    public override async Task DiscordClientOnUserUnbanned(SocketUser user, SocketGuild guild)
    {
        if (GuildConfig(guild.Id).Get<ulong?>(ChannelUnban) is not { } channelId)
            return;
        var channel = await HoardMain.DiscordClient.GetChannelAsync(channelId);
        if (channel is not IMessageChannel messageChannel)
        {
            HoardMain.Logger.LogWarning("Could not fetch channel!");
            return;
        }

        var auditLogEntries = await guild.GetAuditLogsAsync(20, actionType: ActionType.Unban).FlattenAsync();
        var entry = auditLogEntries.FirstOrDefault(logEntry =>
        {
            var data = (BanAuditLogData)logEntry.Data;
            if (data.Target.Id == user.Id)
                return true;
            return false;
        });

        var moderator = entry is not null ? $"<@!{entry.User.Id}>" : "Unknown Moderator";
        await messageChannel.SendMessageAsync(embed: new EmbedBuilder()
            .WithAuthor(user)
            .WithCurrentTimestamp()
            .WithColor(Color.Purple)
            .WithTitle($"{user.Username} was unbanned.")
            .WithDescription($"<@!{user.Id}>")
            .WithFields(
                new EmbedFieldBuilder().WithName("Moderator").WithValue(moderator),
                new EmbedFieldBuilder().WithName("UID").WithValue($"{user.Id}"))
            .WithImageUrl(user.GetAvatarUrl())
            .Build());
    }

    public override async Task DiscordClientOnUserLeft(SocketGuild socketGuild, SocketUser socketUser)
    {
        if (GuildConfig(socketGuild.Id).Get<ulong?>(ChannelLeave) is not { } channelId)
            return;

        if (await socketGuild.GetBanAsync(socketUser) is not null)
            return;

        var channel = await HoardMain.DiscordClient.GetChannelAsync(channelId);
        if (channel is not IMessageChannel messageChannel)
        {
            HoardMain.Logger.LogWarning("Could not fetch channel!");
            return;
        }

        await messageChannel.SendMessageAsync(embed: new EmbedBuilder()
            .WithAuthor(socketUser)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .WithColor(Color.DarkRed)
            .WithTitle($"{socketUser.Username} has left the Guild.")
            .WithDescription($"<@!{socketUser.Id}>")
            .WithImageUrl(socketUser.GetAvatarUrl())
            .WithFields(new EmbedFieldBuilder().WithName("Total Members").WithValue(socketGuild.MemberCount))
            .WithFields(new EmbedFieldBuilder().WithName("Creation Date").WithValue(socketUser.CreatedAt))
            .WithFields(new EmbedFieldBuilder().WithName("UID").WithValue($"{socketUser.Id}"))
            .Build());
    }

    public override async Task DiscordClientOnGuildMemberUpdated(SocketGuildUser oldUserValue, SocketGuildUser newUser)
    {
        // give it a second to update everything
        await Task.Delay(1000);

        ModuleHelper.TryGetModule(out UserDataHelper? userData);

        var rolesRemoved = oldUserValue.Roles.Where(role => !newUser.Roles.Contains(role)).ToList();
        var rolesAdded = newUser.Roles.Where(role => !oldUserValue.Roles.Contains(role)).ToList();
        var somethingChanged = false;

        var flagsRemoved = (from flag in Enum.GetValues<GuildUserFlags>()
            where oldUserValue.Flags.HasFlag(flag)
            where !newUser.Flags.HasFlag(flag)
            select flag).ToList();
        var flagsAdded = (from flag in Enum.GetValues<GuildUserFlags>()
            where newUser.Flags.HasFlag(flag)
            where !oldUserValue.Flags.HasFlag(flag)
            select flag).ToList();

        var embed = new EmbedBuilder()
            .WithAuthor(newUser)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .WithColor(Color.Purple)
            .WithTitle($"{newUser.Username} updated.")
            .WithDescription($"{newUser.Mention}");

        var getAuditType = new List<ActionType>();
        if (rolesRemoved.Any())
        {
            getAuditType.Add(ActionType.MemberRoleUpdated);
            somethingChanged = true;
            var rolesRemovedText = new StringBuilder();
            foreach (var role in rolesRemoved)
                rolesRemovedText.AppendLine($"- {role.Mention}");
            embed.AddField(new EmbedFieldBuilder().WithName("Removed Roles").WithValue(rolesRemovedText.ToString()));
        }

        if (rolesAdded.Any())
        {
            getAuditType.Add(ActionType.MemberRoleUpdated);
            somethingChanged = true;
            var rolesAddedText = new StringBuilder();
            foreach (var role in rolesAdded)
                rolesAddedText.AppendLine($"- {role.Mention}");
            embed.AddField(new EmbedFieldBuilder().WithName("Added Roles").WithValue(rolesAddedText.ToString()));
        }

        if (newUser.Nickname != oldUserValue.Nickname)
        {
            getAuditType.Add(ActionType.MemberUpdated);
            userData?.AddGuildNicknameChange(newUser);
            somethingChanged = true;
            embed.AddField(new EmbedFieldBuilder()
                .WithName("Changed Nickname")
                .WithValue($"`{oldUserValue.Nickname ?? "No Nickname"}` -> `{newUser.Nickname ?? "No Nickname"}`"));
        }

        if (getAuditType.Any())
            foreach (var flag in getAuditType.ToHashSet())
            {
                var auditLogEntries = await ((IGuild)newUser.Guild).GetAuditLogsAsync(20, actionType: flag);
                foreach (var auditEntry in auditLogEntries)
                {
                    switch (auditEntry.Data)
                    {
                        case MemberUpdateAuditLogData memberUpdateData when memberUpdateData.Target.Id != newUser.Id:
                        case MemberRoleAuditLogData roleUpdateData when roleUpdateData.Target.Id != newUser.Id:
                            continue;
                    }

                    embed.AddField(new EmbedFieldBuilder()
                        .WithName($"Audit Entry - {flag.ToString()}")
                        .WithValue(
                            $"Moderator: {auditEntry.User.Mention}\n" +
                            $"Reason:    `{auditEntry.Reason ?? "No Reason Specified"}`\n" +
                            $"Timestamp: <t:{auditEntry.CreatedAt.ToUnixTimeSeconds()}>"));
                    break;
                }
            }

        if (flagsRemoved.Any())
        {
            somethingChanged = true;
            var flagsRemovedText = new StringBuilder();
            foreach (var flag in flagsRemoved)
                flagsRemovedText.AppendLine($"- {flag.ToString()}");
            embed.AddField(new EmbedFieldBuilder().WithName("Removed Flags").WithValue(flagsRemovedText.ToString()));
        }

        if (flagsAdded.Any())
        {
            somethingChanged = true;
            var flagsAddedText = new StringBuilder();
            foreach (var flag in flagsAdded)
                flagsAddedText.AppendLine($"- {flag.ToString()}");
            embed.AddField(new EmbedFieldBuilder().WithName("Added Flags").WithValue(flagsAddedText.ToString()));
        }

        if (!string.Equals(newUser.Username, oldUserValue.Username, StringComparison.CurrentCultureIgnoreCase))
        {
            userData?.AddUsernameChange(newUser);
            somethingChanged = true;
            embed.AddField(new EmbedFieldBuilder()
                .WithName("Changed Username")
                .WithValue($"`{oldUserValue.Username}` -> `{newUser.Username}`"));
        }

        if (oldUserValue.GetDisplayAvatarUrl() != newUser.GetDisplayAvatarUrl())
            userData?.AddProfilePictureChange(newUser);

        var channel = await GetChannel(newUser.Guild.Id, ChannelUpdate);
        if (channel is null)
            return;

        if (somethingChanged)
            await channel.SendMessageAsync(embed: embed.Build());
    }
}