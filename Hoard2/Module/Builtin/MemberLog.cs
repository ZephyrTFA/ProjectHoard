using Discord;
using Discord.Rest;
using Discord.WebSocket;

namespace Hoard2.Module.Builtin
{
	public class MemberLog : ModuleBase
	{
		public MemberLog(string configPath) : base(configPath) { }

		[ModuleCommand("set-member-log-channel", "Update the target channel for member logging", GuildPermission.Administrator)]
		public async Task SetMemberLogChannel(SocketSlashCommand command, IChannel channel)
		{
			GuildConfig(command.GuildId!.Value).Set("log-channel", channel.Id);
			await command.RespondAsync(text: $"Updated the target log channel to <#{channel.Id}>");
		}

		[ModuleCommand("check-member-log-channel", "Check the current target channel", GuildPermission.Administrator)]
		public async Task CheckMemberLogChannel(SocketSlashCommand command)
		{
			var current = GuildConfig(command.GuildId!.Value).Get<ulong?>("log-channel");
			if (current is { })
				await command.RespondAsync($"Currently logging to <#{current}>");
			else
				await command.RespondAsync("Target channel is not set.");
		}

		public override async Task DiscordClientOnUserJoined(SocketGuildUser socketGuildUser)
		{
			if (GuildConfig(socketGuildUser.Guild.Id).Get<ulong?>("log-channel") is not { } channelId)
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

		public override async Task UserBanned(SocketGuild guild, SocketUser user)
		{
			if (GuildConfig(guild.Id).Get<ulong?>("log-channel") is not { } channelId)
				return;
			var channel = await HoardMain.DiscordClient.GetChannelAsync(channelId);
			if (channel is not IMessageChannel messageChannel)
			{
				HoardMain.Logger.LogWarning("Could not fetch channel!");
				return;
			}

			var ban = await guild.GetBanAsync(user);
			await messageChannel.SendMessageAsync(embed: new EmbedBuilder()
				.WithAuthor(user)
				.WithCurrentTimestamp()
				.WithColor(Color.DarkRed)
				.WithTitle($"{user.Username} was banned.")
				.WithDescription($"<@!{user.Id}>")
				.WithFields(
					new EmbedFieldBuilder().WithName("Moderator").WithValue($"<@!{user.Id}>"),
					new EmbedFieldBuilder().WithName("UID").WithValue($"{user.Id}"),
					new EmbedFieldBuilder().WithName("Ban Reason").WithValue(String.IsNullOrWhiteSpace(ban.Reason) ? "No ban reason supplied" : ban.Reason))
				.WithImageUrl(user.GetAvatarUrl())
				.Build());
		}

		public override async Task UserUnbanned(SocketGuild guild, SocketUser user)
		{
			if (GuildConfig(guild.Id).Get<ulong?>("log-channel") is not { } channelId)
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
			if (GuildConfig(socketGuild.Id).Get<ulong?>("log-channel") is not { } channelId)
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
	}
}
