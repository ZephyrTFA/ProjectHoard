using Discord;
using Discord.WebSocket;

namespace Hoard2.Module.Builtin
{
	public class MemberLog : ModuleBase
	{
		public MemberLog(string configPath) : base(configPath) { }

		[ModuleCommand("set-member-log-channel", "Update the target channel for member logging", GuildPermission.Administrator,
			new[] { "channel" },
			new[] { typeof(IChannel) },
			new[] { "id for the channel to use for logging" })]
		public async Task SetMemberLogChannel(SocketSlashCommand command)
		{
			if (command.Data.Options.FirstOrDefault()?.Value is not IChannel channel)
				throw new InvalidDataException();
			ModuleConfig.Set("log-channel", channel.Id);
			await command.RespondAsync(text: $"Updated the target log channel to <#{channel.Id}>");
		}

		public override async Task DiscordClientOnUserJoined(SocketGuildUser socketGuildUser)
		{
			if (ModuleConfig.GetTree(socketGuildUser.Guild.Id.ToString()).Get<ulong?>("log-channel") is not { } channelId)
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
			if (ModuleConfig.GetTree(socketGuild.Id.ToString()).Get<ulong?>("log-channel") is not { } channelId)
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
