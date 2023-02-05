﻿using Discord;
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

		public override async Task DiscordClientOnUserLeft(SocketGuild socketGuild, SocketUser socketUser)
		{
			if (GuildConfig(socketGuild.Id).Get<ulong?>("log-channel") is not { } channelId)
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

		public override async Task DiscordClientOnUserUpdated(SocketGuildUser current, SocketGuildUser old)
		{
			if (GuildConfig(current.Guild.Id).Get<ulong?>("log-channel") is not { } channelId)
				return;
			
			var channel = await HoardMain.DiscordClient.GetChannelAsync(channelId);
			if (channel is not IMessageChannel messageChannel)
			{
				HoardMain.Logger.LogWarning("Could not fetch channel!");
				return;
			}

			var embed = new EmbedBuilder()
				.WithAuthor(current)
				.WithTimestamp(DateTimeOffset.UtcNow)
				.WithColor(Color.DarkBlue)
				.WithTitle($"{current.Username} has been updated.")
				.WithDescription($"<@!{current.Id}>");

			var newRoles = current.Roles.Where(x => !old.Roles.Contains(x)).ToList();
			if (newRoles.Count > 0)
				embed.AddField("New Roles", string.Join(", ", newRoles.Select(x => x.Mention)));

			var oldRoles = old.Roles.Where(x => !current.Roles.Contains(x)).ToList();
			if (oldRoles.Count > 0)
				embed.AddField("Removed Roles", string.Join(", ", oldRoles.Select(x => x.Mention)));

			var oldName = old.Username;
			var newName = current.Username;
			if (oldName != newName)
				embed.AddField("Username", $"{oldName} -> {newName}");

			var oldNickname = old.Nickname;
			var newNickname = current.Nickname;
			if (oldNickname != newNickname)
				embed.AddField("Nickname", $"{oldNickname} -> {newNickname}");

			await messageChannel.SendMessageAsync(embed: embed.Build());
		}
	}
}
