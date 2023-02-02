using System.Diagnostics.CodeAnalysis;

using Discord;
using Discord.WebSocket;

namespace Hoard2.Module.Builtin
{
	public class MessageLog : ModuleBase
	{
		public MessageLog(string configPath) : base(configPath) { }

		public override async Task DiscordClientOnMessageDeleted(IMessage message, IGuildChannel channel)
		{
			var guild = channel.GuildId;
			if (!TryGetChannel(guild, out var logChannel)) return;
			var updateEmbed = new EmbedBuilder()
				.WithAuthor(message.Author)
				.WithTitle("Message Deleted")
				.WithTimestamp(DateTimeOffset.UtcNow)
				.WithColor(Color.Red)
				.AddField("Contents", message.CleanContent)
				.WithFooter($"{message.Id} | <#{channel.Id}>");
			await logChannel.SendMessageAsync(embed: updateEmbed.Build());
		}

		public override async Task DiscordClientOnMessageUpdated(IMessage oldMessage, SocketMessage newMessage, IGuildChannel socketMessageChannel)
		{
			var guild = socketMessageChannel.GuildId;
			if (!TryGetChannel(guild, out var logChannel)) return;
			var updateEmbed = new EmbedBuilder()
				.WithAuthor(newMessage.Author)
				.WithTitle("Message Updated")
				.WithTimestamp(newMessage.EditedTimestamp!.Value)
				.WithColor(Color.Teal)
				.AddField("Previous Contents", oldMessage.CleanContent)
				.WithFooter($"{newMessage.Id} | <#{socketMessageChannel.Id}> | [View]({newMessage.GetJumpUrl()})");
			await logChannel.SendMessageAsync(embed: updateEmbed.Build());
		}

		bool TryGetChannel(ulong guild, [NotNullWhen(true)] out ISocketMessageChannel? channel)
		{
			channel = null;
			if (!GuildConfig(guild).TryGet("log-channel", out ulong channelId))
				return false;
			channel = HoardMain.DiscordClient.GetChannelAsync(channelId).Preserve().GetAwaiter().GetResult() as ISocketMessageChannel;
			return channel is not null;
		}

		[ModuleCommand("set-log-channel", "sets the channel used for logging", GuildPermission.Administrator)]
		public async Task SetLogChannel(SocketSlashCommand command, IChannel channel)
		{
			GuildConfig(command.GuildId!.Value).Set("log-channel", channel.Id);
			await command.RespondAsync($"Updated channel to <#{channel.Id}>");
		}

		[ModuleCommand("get-log-channel", "sets the channel used for logging", GuildPermission.Administrator)]
		public async Task GetLogChannel(SocketSlashCommand command)
		{
			if (!GuildConfig(command.GuildId!.Value).TryGet("log-channel", out ulong channelId))
				await command.RespondAsync("Log channel is not set.");
			else
				await command.RespondAsync($"Log channel is <#{channelId}>");
		}
	}
}
