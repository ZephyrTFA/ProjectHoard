using System.ComponentModel;

using Discord;
using Discord.Rest;
using Discord.WebSocket;

namespace Hoard2.Module.Builtin
{
	public class MemberLog : ModuleBase
	{
		public const string ChannelJoin = "channel-join";
		public const string ChannelLeave = "channel-leave";
		public const string ChannelBan = "channel-ban";
		public const string ChannelUnban = "channel-unban";

		public MemberLog(string configPath) : base(configPath) { }

		[ModuleCommand(GuildPermission.Administrator)]
		[Description("Update the target channel for member joins.")]
		public async Task SetJoinChannel(SocketSlashCommand command, IChannel channel)
		{
			GuildConfig(command.GuildId!.Value).Set(ChannelJoin, channel.Id);
			await command.RespondAsync(text: $"Updated the target log channel to <#{channel.Id}>");
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
			GuildConfig(command.GuildId!.Value).Set(ChannelUnban, channel.Id);
			await command.RespondAsync($"Updated the target log channel to <#{channel.Id}>");
		}

		[ModuleCommand(GuildPermission.Administrator)]
		[Description("Update the target channel for member bans.")]
		public async Task SetUnbanChannel(SocketSlashCommand command, IChannel channel)
		{
			GuildConfig(command.GuildId!.Value).Set(ChannelBan, channel.Id);
			await command.RespondAsync($"Updated the target log channel to <#{channel.Id}>");
		}

		async Task<IMessageChannel?> GetChannel(ulong guild, string key)
		{
			if (!GuildConfig(guild).TryGet<ulong>(key, out var channelId))
				return null;
			if (channelId == 0)
				return null;
			return await HoardMain.DiscordClient.GetChannelAsync(channelId) as IMessageChannel;
		}

		[ModuleCommand(GuildPermission.Administrator)]
		[Description("Check the current target channel.")]
		public async Task CheckMemberLogChannel(SocketSlashCommand command)
		{
			var joinChannel = await GetChannel(command.GuildId!.Value, ChannelJoin);
			var leaveChannel = await GetChannel(command.GuildId!.Value, ChannelLeave);
			var banChannel = await GetChannel(command.GuildId!.Value, ChannelBan);
			var unbanChannel = await GetChannel(command.GuildId!.Value, ChannelUnban);

			var joinText = joinChannel is { } ? $"<#{joinChannel.Id}>" : "Not Set";
			var leaveText = leaveChannel is { } ? $"<#{leaveChannel.Id}>" : "Not Set";
			var banText = banChannel is { } ? $"<#{banChannel.Id}>" : "Not Set";
			var unbanText = unbanChannel is { } ? $"<#{unbanChannel.Id}>" : "Not Set";

			await command.RespondAsync("Channel Map", embed:
				new EmbedBuilder()
					.WithCurrentTimestamp()
					.WithTitle("Channel Map")
					.WithDescription(
						$"**Join:**  - {joinText}\n" +
						$"**Leave:** - {leaveText}\n" +
						$"**Ban:**   - {banText}\n" +
						$"**Unban:** - {unbanText}\n"
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
				var data = (UnbanAuditLogData)logEntry.Data;
				if (data.Target.Id == user.Id)
					return true;
				return false;
			});
			var moderator = entry is { } ? $"<@!{entry.User.Id}>" : "Unknown Moderator";
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
					new EmbedFieldBuilder().WithName("Ban Reason").WithValue(String.IsNullOrWhiteSpace(ban.Reason) ? "No ban reason supplied" : ban.Reason))
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
				var data = (UnbanAuditLogData)logEntry.Data;
				if (data.Target.Id == user.Id)
					return true;
				return false;
			});

			var moderator = entry is { } ? $"<@!{entry.User.Id}>" : "Unknown Moderator";
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

			if (await socketGuild.GetBanAsync(socketUser) is { })
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
	}
}
