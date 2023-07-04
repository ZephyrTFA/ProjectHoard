using System.Text;

using Discord;
using Discord.WebSocket;

namespace Hoard2.Module.Builtin.Moderation
{
	public class MemberFlagger : ModuleBase
	{
		public MemberFlagger(string configPath) : base(configPath) { }

		async Task<IMessageChannel?> GetLogChannel(ulong guild)
		{
			var channelId = GuildConfig(guild).Get<ulong>("log-channel");
			if (channelId == default)
				return null;
			return await HoardMain.DiscordClient.GetChannelAsync(channelId) as IMessageChannel;
		}

		void SetLogChannel(ulong guild, ulong channelId) => GuildConfig(guild).Set("log-channel", channelId);

		List<ulong> GetIgnoreList(ulong guild) => GuildConfig(guild).Get("ignore-list", new List<ulong>())!;

		void SetIgnoreList(ulong guild, List<ulong> list) => GuildConfig(guild).Set("ignore-list", list);

		IRole? GetFlagRole(ulong guild)
		{
			var roleId = GuildConfig(guild).Get<ulong>("flag-role");
			if (roleId == default)
				return null;
			return HoardMain.DiscordClient.GetGuild(guild).GetRole(roleId);
		}

		void SetFlagRole(ulong guild, ulong roleId) => GuildConfig(guild).Set("flag-role", roleId);

		[ModuleCommand(GuildPermission.Administrator)]
		[CommandGuildOnly]
		public async Task SetLogChannel(SocketSlashCommand command, IMessageChannel channel)
		{
			SetLogChannel(command.GuildId!.Value, channel.Id);
			await command.RespondAsync($"Set the log channel to: <#{channel.Id}>");
		}

		[ModuleCommand(GuildPermission.Administrator)]
		[CommandGuildOnly]
		public async Task SetFlagRole(SocketSlashCommand command, IRole role)
		{
			SetFlagRole(command.GuildId!.Value, role.Id);
			await command.RespondAsync($"Set the flag role to: {role.Mention}", allowedMentions: AllowedMentions.None);
		}

		public override async Task DiscordClientOnUserJoined(SocketGuildUser socketGuildUser) => await ProcessGuildUser(socketGuildUser);

		public override Task DiscordClientOnUserUpdated(SocketUser oldUser, SocketUser newUser)
		{
			if (newUser is not IGuildUser guildNewUser)
				return Task.CompletedTask;
			if (oldUser is not IGuildUser guildOldUser)
				return Task.CompletedTask;
			if (GetFlagRole(guildNewUser.GuildId) is not { } flagRole)
				return Task.CompletedTask;

			var ignoreList = GetIgnoreList(guildNewUser.GuildId);
			if (guildNewUser.RoleIds.Contains(flagRole.Id))
			{
				if (ignoreList.Contains(guildNewUser.Id))
				{
					ignoreList.Remove(guildNewUser.Id);
					SetIgnoreList(guildNewUser.Id, ignoreList);
				}
				return Task.CompletedTask;
			}

			if (!guildOldUser.RoleIds.Contains(flagRole.Id))
				return Task.CompletedTask;
			if (ignoreList.Contains(guildNewUser.Id))
				return Task.CompletedTask;

			ignoreList.Add(guildNewUser.Id);
			SetIgnoreList(guildNewUser.GuildId, ignoreList);
			return Task.CompletedTask;
		}

		async Task ProcessGuildUser(IGuildUser user)
		{
			if (GetIgnoreList(user.GuildId).Contains(user.Id))
				return;
			if (GetFlagRole(user.GuildId) is not { } flagRole)
				return;
			if (await GetLogChannel(user.GuildId) is not { } flagChannel)
				return;

			if (user.RoleIds.Contains(flagRole.Id))
				return;

			var failReasons = new List<string>();

			if (user.GetAvatarUrl() is null)
				failReasons.Add("Avatar is not set.");

			var monthAgoOffset = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromDays(30));
			if (user.CreatedAt.CompareTo(monthAgoOffset) >= 0)
				failReasons.Add("Account is less than 30 days old.");

			var isUnlucky = Random.Shared.Next(0, 1001) == 0;
			if (isUnlucky)
				failReasons.Add("Account was determined to be unlucky.");

			if (!failReasons.Any())
				return;

			await user.AddRoleAsync(flagRole);
			var message = new StringBuilder($"{user.Mention} has been flagged for the following reasons:\n");
			foreach (var failReason in failReasons)
				message.AppendLine($"- {failReason}");
			await flagChannel.SendMessageAsync(message.ToString());
		}
	}
}
