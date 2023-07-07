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

		public override async Task DiscordClientOnGuildMemberUpdated(SocketGuildUser oldUser, SocketGuildUser newUser)
		{
			if (GetFlagRole(newUser.Guild.Id) is not { } flagRole)
				return;

			var hadBefore = oldUser.Roles.Any(role => role.Id == flagRole.Id);
			var hasAfter = newUser.Roles.Any(role => role.Id == flagRole.Id);

			var ignoreList = GetIgnoreList(newUser.Guild.Id);
			if (hasAfter && !hadBefore)
			{
				if (ignoreList.Contains(newUser.Id))
				{
					ignoreList.Remove(newUser.Id);
					SetIgnoreList(newUser.Id, ignoreList);
				}
				await ProcessGuildUser(newUser, true);
				return;
			}

			if (hadBefore == hasAfter)
				return;
			if (ignoreList.Contains(newUser.Id))
				return;

			ignoreList.Add(newUser.Id);
			SetIgnoreList(newUser.Guild.Id, ignoreList);
		}

		async Task ProcessGuildUser(IGuildUser user, bool forced = false)
		{
			if (GetIgnoreList(user.GuildId).Contains(user.Id) && !forced)
				return;
			if (GetFlagRole(user.GuildId) is not { } flagRole)
				return;
			if (await GetLogChannel(user.GuildId) is not { } flagChannel)
				return;

			if (user.RoleIds.Contains(flagRole.Id) && !forced)
				return;

			var failReasons = new List<string>();

			if (forced)
				failReasons.Add("Forced.");

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
