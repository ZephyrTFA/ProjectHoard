using Discord;

namespace Hoard2.Module.Builtin
{
	public class Kek : ModuleBase
	{
		public Kek(ulong guildId, string configPath) : base(guildId, configPath) { }

		public override async Task DiscordClientOnMessageReceived(IMessage arg)
		{
			if (arg.Source != MessageSource.User)
				return;

			if (arg.Content.ToLower() == "kek")
				await arg.Channel.SendMessageAsync("Kek");
		}
	}
}
