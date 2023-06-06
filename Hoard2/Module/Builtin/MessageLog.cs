using System.Diagnostics.CodeAnalysis;
using System.Text;

using Discord;
using Discord.WebSocket;

namespace Hoard2.Module.Builtin
{
	public class MessageLog : ModuleBase
	{
		public MessageLog(string configPath) : base(configPath) { }

		public override async Task DiscordClientOnMessageDeleted(IMessage message, IGuildChannel channel)
		{
			if (IsChannelIgnored(channel.Id, channel.GuildId))
				return;
			var guild = channel.GuildId;
			if (!TryGetChannel(guild, out var logChannel)) return;
			var updateEmbed = new EmbedBuilder()
				.WithAuthor(message.Author)
				.WithTitle("Message Deleted")
				.WithTimestamp(DateTimeOffset.UtcNow)
				.WithColor(Color.Red)
				.AddField("Contents", message.CleanContent)
				.AddField("Author", $"{message.Author.Mention} ({message.Author.Id})")
				.AddField("Jump Information", $"<#{channel.Id}>");
			await logChannel.SendMessageAsync(embed: updateEmbed.Build());
		}

		public override async Task DiscordClientOnMessageUpdated(IMessage oldMessage, SocketMessage newMessage, IGuildChannel socketMessageChannel)
		{
			if (IsChannelIgnored(socketMessageChannel.Id, socketMessageChannel.GuildId))
				return;

			var guild = socketMessageChannel.GuildId;
			if (!TryGetChannel(guild, out var logChannel)) return;
			var updateEmbed = new EmbedBuilder()
				.WithAuthor(newMessage.Author)
				.WithTitle("Message Updated")
				.WithTimestamp(newMessage.EditedTimestamp!.Value)
				.WithColor(Color.Teal)
				.AddField("Previous Contents", oldMessage.CleanContent)
				.AddField("Author", $"{newMessage.Author.Mention} ({newMessage.Author.Id})")
				.AddField("Jump Information", $"<#{socketMessageChannel.Id}> | [View]({newMessage.GetJumpUrl()})");
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

		[ModuleCommand("ignore-channel", "ignore a specific channel", GuildPermission.Administrator)]
		public async Task IgnoreChannel(SocketSlashCommand command, IChannel channel)
		{
			await command.DeferAsync();
			SetIgnoredChannel(channel.Id, command.GuildId!.Value, true);
			await command.ModifyOriginalResponse($"<#{channel.Id}> is now ignored.");
		}

		[ModuleCommand("get-ignored", "get all ignored channels", GuildPermission.Administrator)]
		public async Task GetIgnored(SocketSlashCommand command)
		{
			await command.DeferAsync();
			var ignored = GuildConfig(command.GuildId!.Value).Get("ignored-channels", new List<ulong>())!;
			var resp = new StringBuilder("Ignored Channels:\n");
			foreach (var channel in ignored)
				resp.AppendLine($"- <#{channel}>");
			await command.ModifyOriginalResponse(resp.ToString());
		}

		[ModuleCommand("clear-ignores", "removes all ignored channels", GuildPermission.Administrator)]
		public async Task ClearIgnores(SocketSlashCommand command)
		{
			var config = GuildConfig(command.GuildId!.Value);
			var ignored = config.Get("ignored-channels", new List<ulong>())!;
			var resp = new StringBuilder("Removed Ignored Channels:\n");
			foreach (var channel in ignored)
				resp.AppendLine($"- <#{channel}>");

			ignored.Clear();
			config.Set("ignored-channels", ignored);
			await command.ModifyOriginalResponse(resp.ToString());
		}

		bool IsChannelIgnored(ulong channel, ulong guild)
		{
			var ignored = GuildConfig(guild).Get("ignored-channels", new List<ulong>());
			if (ignored!.Contains(channel)) return true;

			var guildInstance = HoardMain.DiscordClient.GetGuild(guild);
			return ignored
				.Select(ignoredCategory => guildInstance.GetCategoryChannel(ignoredCategory))
				.Where(value => value is not null)
				.Any(category => category.Channels.Any(inner => inner.Id == channel));
		}

		void SetIgnoredCategory(ulong category, ulong guild, bool ignore)
		{
			var config = GuildConfig(guild);
			var ignored = config.Get("ignored-categories", new List<ulong>())!;
			switch (ignore)
			{
				case true when ignored.Contains(category):   return;
				case false when !ignored.Contains(category): return;
				
				case true:
					ignored.Add(category);
					break;
				
				case false:
					ignored.Remove(category);
					break;
			}
			
			config.Set("ignored-categories", ignored);
		}

		void SetIgnoredChannel(ulong channel, ulong guild, bool ignore)
		{
			var config = GuildConfig(guild);
			var ignored = config.Get("ignored-channels", new List<ulong>())!;
			switch (ignore)
			{
				case true when ignored.Contains(channel):   return;
				case false when !ignored.Contains(channel): return;

				case true:
					ignored.Add(channel);
					break;

				case false:
					ignored.Remove(channel);
					break;
			}

			config.Set("ignored-channels", ignored);
		}

		[ModuleCommand("un-ignore-channel", "removes a channel from the ignore list", GuildPermission.Administrator)]
		public async Task UnIgnoreChannel(SocketSlashCommand command, IChannel channel)
		{
			await command.DeferAsync();
			SetIgnoredChannel(channel.Id, command.GuildId!.Value, false);
			await command.ModifyOriginalResponse($"<#{channel.Id}> is no longer ignored.");
		}
	}
}
