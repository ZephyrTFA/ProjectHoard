using Discord;
using Discord.WebSocket;

namespace Hoard2.Module.Builtin
{
	public class MemberLog : ModuleBase
	{
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

		[ModuleCommand("debug", "debug something", GuildPermission.Administrator,
			new[] { "debug-target" },
			new[] { typeof(string) },
			new[] { "debugging target" })]
		public async Task DebugCommand(SocketSlashCommand command)
		{
			switch (command.Data.Options.FirstOrDefault()?.Value ?? String.Empty)
			{
				case "join":
					if (ModuleConfig.Get<ulong?>("log-channel") is not { })
					{
						await command.RespondAsync("Log Channel not set");
						return;
					}

					await DiscordClientOnUserJoined(command.User as SocketGuildUser ?? throw new InvalidOperationException());
					await command.RespondAsync("Debugging Join");
					return;
				
				case "leave":
					if (ModuleConfig.Get<ulong?>("log-channel") is not { })
					{
						await command.RespondAsync("Log Channel not set");
						return;
					}

					await DiscordClientOnUserLeft(command.User);
					await command.RespondAsync("Debugging Leave");
					return;

				default:
					await command.RespondAsync("Not implemented");
					return;
			}
		}

		public MemberLog(ulong guildId, string configPath) : base(guildId, configPath) { }

		public override async Task DiscordClientOnUserJoined(SocketGuildUser arg)
		{
			if (ModuleConfig.Get<ulong?>("log-channel") is not { } channelId)
				return;
			var channel = await HoardMain.DiscordClient.GetChannelAsync(channelId);
			if (channel is not IMessageChannel messageChannel)
			{
				HoardMain.Logger.LogWarning("Could not fetch channel!");
				return;
			}

			await messageChannel.SendMessageAsync(embed: new EmbedBuilder()
				.WithAuthor(arg)
				.WithTimestamp(DateTimeOffset.UtcNow)
				.WithColor(Color.Blue)
				.WithTitle($"{arg.Username} has joined the Guild.")
				.WithDescription($"<@!{arg.Id}>")
				.WithImageUrl(arg.GetDisplayAvatarUrl())
				.WithFields(new EmbedFieldBuilder().WithName("Total Members").WithValue(arg.Guild.MemberCount))
				.WithFields(new EmbedFieldBuilder().WithName("Creation Date").WithValue(arg.CreatedAt))
				.WithFields(new EmbedFieldBuilder().WithName("UID").WithValue($"{arg.Id}"))
				.Build());
		}

		public override async Task DiscordClientOnUserLeft(SocketUser arg)
		{
			if (ModuleConfig.Get<ulong?>("log-channel") is not { } channelId)
				return;
			var channel = await HoardMain.DiscordClient.GetChannelAsync(channelId);
			if (channel is not IMessageChannel messageChannel)
			{
				HoardMain.Logger.LogWarning("Could not fetch channel!");
				return;
			}

			await messageChannel.SendMessageAsync(embed: new EmbedBuilder()
				.WithAuthor(arg)
				.WithTimestamp(DateTimeOffset.UtcNow)
				.WithColor(Color.DarkRed)
				.WithTitle($"{arg.Username} has left the Guild.")
				.WithDescription($"<@!{arg.Id}>")
				.WithImageUrl(arg.GetAvatarUrl())
				.WithFields(new EmbedFieldBuilder().WithName("Total Members").WithValue(HoardMain.DiscordClient.GetGuild(GuildID).MemberCount))
				.WithFields(new EmbedFieldBuilder().WithName("Creation Date").WithValue(arg.CreatedAt))
				.WithFields(new EmbedFieldBuilder().WithName("UID").WithValue($"{arg.Id}"))
				.Build());
		}
	}
}
