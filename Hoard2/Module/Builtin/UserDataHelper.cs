using System.ComponentModel;

using Discord.WebSocket;

namespace Hoard2.Module.Builtin
{
	public class UserDataHelper : ModuleBase
	{
		public UserDataHelper(string configPath) : base(configPath) { }

		public ModuleConfig GetUserConfig(ulong user, string module) => CustomConfig($"{user}-{module}-{0}");

		public ModuleConfig GetGuildUserConfig(ulong user, ulong guild, string module) => CustomConfig($"{user}-{module}-{guild}");

		[ModuleCommand]
		[Description("Get all of your stored personal data.")]
		public async Task GetUserData(SocketSlashCommand command) => await command.RespondAsync("Not implemented");
	}
}
