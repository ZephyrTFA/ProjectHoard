using System.Diagnostics.CodeAnalysis;

using Discord;
using Discord.WebSocket;

using JetBrains.Annotations;

namespace Hoard2.Module
{
	[UsedImplicitly(ImplicitUseTargetFlags.WithMembers | ImplicitUseTargetFlags.WithInheritors)]
	[SuppressMessage("Performance", "CA1822:Mark members as static")]
	public class ModuleBase
	{
		public ulong GuildID { get; init; }

		public ModuleBase(ulong guildId, string configPath)
		{
			GuildID = guildId;
			ModuleConfig = new ModuleConfig(configPath);
		}

		protected ModuleConfig ModuleConfig { get; init; }

		public virtual Task DiscordClientOnSlashCommandExecuted(SocketSlashCommand arg) => Task.CompletedTask;

		public virtual Task DiscordClientOnUserLeft(SocketUser arg) => Task.CompletedTask;

		public virtual Task DiscordClientOnUserJoined(SocketGuildUser arg) => Task.CompletedTask;

		public virtual Task DiscordClientOnMessageUpdated(Cacheable<IMessage, ulong> arg1, SocketMessage arg2, ISocketMessageChannel arg3) => Task.CompletedTask;

		public virtual Task DiscordClientOnMessageDeleted(Cacheable<IMessage, ulong> arg1, Cacheable<IMessageChannel, ulong> arg2) => Task.CompletedTask;

		public virtual Task DiscordClientOnMessageReceived(IMessage arg) => Task.CompletedTask;
	}

	[AttributeUsage(AttributeTargets.Method)]
	[UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
	public class ModuleCommandAttribute : Attribute
	{
		public string CommandName { get; init; }

		public string CommandDescription { get; init; }

		public GuildPermission CommandPermissionRequirements { get; init; }

		public string[] CommandParamNames { get; init; } = Array.Empty<string>();

		public Type[] CommandParamTypes { get; init; } = Array.Empty<Type>();

		public string[] CommandParamDescriptions { get; init; } = Array.Empty<string>();

		public ModuleCommandAttribute(string commandName,
																	string commandDescription,
																	GuildPermission commandPermissionRequirements,
																	string[]? commandParamNames = null,
																	Type[]? commandParamTypes = null,
																	string[]? commandParamDescriptions = null)
		{
			CommandName = commandName.ToLower();
			CommandDescription = commandDescription;
			CommandPermissionRequirements = commandPermissionRequirements;
			if (commandParamNames is { }) CommandParamNames = commandParamNames;
			if (commandParamTypes is { }) CommandParamTypes = commandParamTypes;
			if (commandParamDescriptions is { }) CommandParamDescriptions = commandParamDescriptions;
		}
	}
}
