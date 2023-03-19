using System.Text;

using Discord;
using Discord.WebSocket;

namespace Hoard2.Module.Builtin
{
	public class RulesManager : ModuleBase
	{
		public RulesManager(string configPath) : base(configPath) { }

		List<string> GetRules(IMessageChannel channel, ulong guild)
		{
			if (!GuildConfig(guild).TryGet<List<string>>($"rules-{channel.Id}", out var rules))
				return new List<string>();
			return rules;
		}

		void SetRules(IMessageChannel channel, ulong guild, List<string> rules)
		{
			GuildConfig(guild).Set($"rules-{channel.Id}", rules);
		}

		string? GetRuleHeader(IMessageChannel channel, ulong guild)
		{
			if (!GuildConfig(guild).TryGet<string>($"header-{channel.Id}", out var header))
				return null;
			return header;
		}

		void SetRuleHeader(IMessageChannel channel, ulong guild, string header)
		{
			GuildConfig(guild).Set($"header-{channel.Id}", header);
		}

		void ClearRuleHeader(IMessageChannel channel, ulong guild)
		{
			GuildConfig(guild).Remove("header-{channel.Id}");
		}

		IUserMessage? GetRuleMessage(IMessageChannel channel, ulong guild)
		{
			if (!GuildConfig(guild).TryGet($"rule-message-{channel.Id}", out ulong message)) return null;
			return channel.GetMessageAsync(message).GetAwaiter().GetResult() as IUserMessage;
		}

		void DeleteRuleMessage(IMessageChannel channel, ulong guild)
		{
			if (!GuildConfig(guild).TryGet($"rule-message-{channel.Id}", out ulong message)) return;
			if (channel.GetMessageAsync(message).GetAwaiter().GetResult() is IUserMessage actual)
				actual.DeleteAsync().Wait();
			GuildConfig(guild).Remove($"rule-message-{channel.Id}");
		}

		IUserMessage CacheRuleMessage(IMessageChannel channel, ulong guild)
		{
			if (GetRuleMessage(channel, guild) is { } message) return message;
			var messageActual = channel.SendMessageAsync("caching").GetAwaiter().GetResult();
			GuildConfig(guild).Set($"rule-message-{channel.Id}", messageActual.Id);
			return messageActual;
		}

		[ModuleCommand("Add a rule to the given channel, optionally on top", GuildPermission.Administrator)]
		public async Task AddRule(SocketSlashCommand command, string rule, IMessageChannel channel, long insertAt = -1)
		{
			var rules = GetRules(channel, command.GuildId!.Value);
			if (insertAt == -1)
				insertAt = rules.Count;
			else if (insertAt > rules.Count)
			{
				await command.RespondAsync("Bad insertion point.", ephemeral: true);
				return;
			}

			rules.Insert((int)insertAt, rule);
			SetRules(channel, command.GuildId!.Value, rules);
			await command.RespondAsync("Done");
		}

		[ModuleCommand("Remove the specified rule from the given channel", GuildPermission.Administrator)]
		public async Task RemoveRule(SocketSlashCommand command, IMessageChannel channel, long ruleId)
		{
			var rules = GetRules(channel, command.GuildId!.Value);
			if (ruleId > rules.Count)
			{
				await command.RespondAsync("Bad rule ID.", ephemeral: true);
				return;
			}

			rules.RemoveAt((int)ruleId);
			SetRules(channel, command.GuildId!.Value, rules);
			await command.RespondAsync("Done");
		}

		[ModuleCommand("Sets the rule header for the channel", GuildPermission.Administrator)]
		public async Task SetHeader(SocketSlashCommand command, IMessageChannel channel, string header)
		{
			SetRuleHeader(channel, command.GuildId!.Value, header);
			await command.RespondAsync("Updated");
		}

		[ModuleCommand("Removes the rule header for the channel", GuildPermission.Administrator)]
		public async Task RemoveHeader(SocketSlashCommand command, IMessageChannel channel)
		{
			ClearRuleHeader(channel, command.GuildId!.Value);
			await command.RespondAsync("Removed");
		}

		[ModuleCommand("Refreshes the rules for the specified (or current) channel", GuildPermission.Administrator)]
		public async Task Refresh(SocketSlashCommand command, IMessageChannel? channel = null, bool deleteAndRecreate = false)
		{
			channel ??= command.Channel;
			var rules = GetRules(channel, command.GuildId!.Value);
			if (rules.Count == 0)
			{
				DeleteRuleMessage(channel, command.GuildId.Value);
				await command.RespondAsync("No rules set.", ephemeral: true);
				return;
			}

			if (deleteAndRecreate)
				DeleteRuleMessage(channel, command.GuildId.Value);

			var ruleMessage = CacheRuleMessage(channel, command.GuildId!.Value);

			var ruleText = new StringBuilder();
			if (GetRuleHeader(channel, command.GuildId.Value) is { } header)
				ruleText.AppendLine(header);
			for (var ruleIdx = 0; ruleIdx < rules.Count; ruleIdx++)
				ruleText.AppendLine($"{ruleIdx} - {rules[ruleIdx]}");

			await ruleMessage.ModifyAsync(props => props.Content = ruleText.ToString());
			await command.RespondAsync("Done");
		}
	}
}
