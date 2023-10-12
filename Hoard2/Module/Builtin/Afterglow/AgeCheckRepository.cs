using Discord;
using Discord.WebSocket;

using Hoard2.Util;

namespace Hoard2.Module.Builtin.Afterglow
{
	public class AgeCheckRepository : ModuleBase
	{
		public AgeCheckRepository(string configPath) : base(configPath) { }

		public override List<Type> GetConfigKnownTypes() => new List<Type>
		{
			typeof(List<AgeCheckInformation>),
			typeof(AgeCheckInformation),
		};

		private List<AgeCheckInformation> GetVerifications(ulong guild) => GuildConfig(guild).Get("age-checks", new List<AgeCheckInformation>())!;

		private void SetVerifications(ulong guild, List<AgeCheckInformation> checks) => GuildConfig(guild).Set("age-checks", checks);

		private async Task ApplyVerificationRolesTo(SocketGuildUser user, bool ageChecked)
		{
			var config = GuildConfig(user.Guild.Id);
			var verifiedRole = config.Get<ulong>("role-verify");
			var ageCheckRole = config.Get<ulong>("role-age-check");
			var toAdd = new List<ulong>();

			var existing = user.Roles.Select(role => role.Id).ToList();
			if (!existing.Contains(verifiedRole))
				toAdd.Add(verifiedRole);
			if (ageChecked && !existing.Contains(ageCheckRole))
				toAdd.Add(ageCheckRole);
			await user.AddRolesAsync(toAdd);
		}

		[ModuleCommand(GuildPermission.Administrator)]
		[CommandGuildOnly]
		public async Task SetVerificationRole(SocketSlashCommand command, IRole role)
		{
			GuildConfig(command.GuildId!.Value).Set("role-verify", role.Id);
			await command.RespondAsync($"Updated the verification role to <@&{role.Id}>.", allowedMentions: AllowedMentions.None);
		}

		[ModuleCommand(GuildPermission.Administrator)]
		[CommandGuildOnly]
		public async Task SetAgeCheckedRole(SocketSlashCommand command, IRole role)
		{
			GuildConfig(command.GuildId!.Value).Set("role-age-check", role.Id);
			await command.RespondAsync($"Updated the age check role to <@&{role.Id}>.", allowedMentions: AllowedMentions.None);
		}

		[ModuleCommand(GuildPermission.ManageRoles)]
		[CommandGuildOnly]
		public async Task VerifyMember(SocketSlashCommand command, IUser user, bool ageChecked = false)
		{
			var checkStore = GetVerifications(command.GuildId!.Value);
			var existing = checkStore.FirstOrDefault(entry => entry.Verified == user.Id);

			if (existing is { } && ageChecked && !existing.AgeChecked)
			{
				checkStore.Remove(existing);
				existing = null;
			}

			if (existing is { })
			{
				await command.RespondAsync($"{user.Mention} is already verified.", ephemeral: true, allowedMentions: AllowedMentions.None);
				await ApplyVerificationRolesTo((SocketGuildUser)user, existing.AgeChecked);
				return;
			}

			var aci = new AgeCheckInformation
			{
				Verified = user.Id,
				Verifier = command.User.Id,
				When = new DateTimeOffset(DateTime.Now).ToUnixTimeSeconds(),
				AgeChecked = ageChecked,
			};
			checkStore.Add(aci);
			SetVerifications(command.GuildId.Value, checkStore);

			await command.SendOrModifyOriginalResponse($"Verified{(ageChecked ? " and Age Checked" : "")} {user.Mention}.", AllowedMentions.None);
			await ApplyVerificationRolesTo((SocketGuildUser)user, ageChecked);
		}

		[ModuleCommand(GuildPermission.ManageRoles)]
		[CommandGuildOnly]
		public async Task GetVerifiedStatus(SocketSlashCommand command, IUser user)
		{
			var verifications = GetVerifications(command.GuildId!.Value);
			if (verifications.FirstOrDefault(entry => entry.Verified == user.Id) is not { } verifyEntry)
			{
				await command.SendOrModifyOriginalResponse($"{user.Mention} is NOT verified!", AllowedMentions.None);
				return;
			}

			await command.SendOrModifyOriginalResponse($"{user.Mention} was verified{(verifyEntry.AgeChecked ? " and age checked" : "")} by <@{verifyEntry.Verifier}>(<t:{verifyEntry.When}:R>).", AllowedMentions.None);
		}

		public class AgeCheckInformation
		{
			public ulong Verified { get; init; }

			public ulong Verifier { get; init; }

			public long When { get; init; }

			public bool AgeChecked { get; init; }
		}
	}
}
