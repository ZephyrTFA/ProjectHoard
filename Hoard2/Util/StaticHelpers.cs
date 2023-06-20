using System.Text;

using Discord;
using Discord.WebSocket;

namespace Hoard2.Util
{
	public static class StaticHelpers
	{
		public static ulong GetGuildId(this IChannel channel)
		{
			if (channel is not IGuildChannel guildChannel)
				return 0;
			return guildChannel.GuildId;
		}

		public static async Task SendOrModifyOriginalResponse(this SocketSlashCommand command, string message, AllowedMentions? allowedMentions = null)
		{
			if (command.HasResponded)
				await command.ModifyOriginalResponseAsync(props =>
				{
					props.Content = message;
					if (allowedMentions is { })
						props.AllowedMentions = allowedMentions;
				});
			else
				await command.RespondAsync(message, allowedMentions: allowedMentions);
		}

		public static bool IsNullableType(this Type type) => Nullable.GetUnderlyingType(type) != null;

		public static ApplicationCommandOptionType ToDiscordCommandType(this Type type)
		{
			if (IsNullableType(type))
				type = Nullable.GetUnderlyingType(type)!;

			if (type == typeof(string))
				return ApplicationCommandOptionType.String;
			if (type == typeof(int) || type == typeof(long))
				return ApplicationCommandOptionType.Integer;
			if (type == typeof(IUser))
				return ApplicationCommandOptionType.User;
			if (type == typeof(IChannel) || type == typeof(IMessageChannel))
				return ApplicationCommandOptionType.Channel;
			if (type == typeof(IRole))
				return ApplicationCommandOptionType.Role;
			if (type == typeof(bool))
				return ApplicationCommandOptionType.Boolean;

			throw new ArgumentException($"cannot convert {type} to option type", nameof(type));
		}


		public static string GetNormalizedRepresentation(this Type moduleType) => GetNormalizedRepresentation(moduleType.Name);

		public static string GetNormalizedRepresentation(this string moduleName)
		{
			var output = new StringBuilder();
			var upperLast = false;
			var first = true;
			foreach (var letter in moduleName)
			{
				var isUpper = Char.IsUpper(letter);
				if (!first)
				{
					if (isUpper && !upperLast)
					{
						output.Append('-');
					}
				}
				else
					first = false;
				upperLast = isUpper;
				output.Append(Char.ToLower(letter));
			}

			return output.ToString();
		}
	}
}
