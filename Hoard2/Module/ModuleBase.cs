using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

using Discord;
using Discord.WebSocket;

using Hoard2.Util;

using JetBrains.Annotations;

namespace Hoard2.Module
{
	[UsedImplicitly(ImplicitUseTargetFlags.WithMembers | ImplicitUseTargetFlags.WithInheritors)]
	[SuppressMessage("Performance", "CA1822:Mark members as static")]
	public class ModuleBase
	{
		string _configDirectory;

		List<Guid> _knownButtons = new List<Guid>();

		List<Guid> _knownMenus = new List<Guid>();
		public ModuleBase(string configPath)
		{
			_configDirectory = configPath;
			GlobalConfig = new ModuleConfig(Path.Join(_configDirectory, "0.xml"));
		}

		protected ModuleConfig GlobalConfig { get; init; }

		public string GetModuleName() => GetType().GetNormalizedRepresentation();

		public ModuleConfig GuildConfig(ulong guild) => new ModuleConfig(Path.Join(_configDirectory, $"{guild}.xml"), GetConfigKnownTypes());

		public ModuleConfig CustomConfig(string key) => new ModuleConfig(Path.Join(_configDirectory, "custom", key), GetConfigKnownTypes());

		public virtual Task DiscordClientOnSlashCommandExecuted(SocketSlashCommand socketSlashCommand) => Task.CompletedTask;

		public virtual Task DiscordClientOnUserLeft(SocketGuild socketGuild, SocketUser socketUser) => Task.CompletedTask;

		public virtual Task DiscordClientOnUserJoined(SocketGuildUser socketGuildUser) => Task.CompletedTask;

		public virtual Task DiscordClientOnUserUpdated(SocketUser oldUser, SocketUser newUser) => Task.CompletedTask;

		public virtual Task DiscordClientOnJoinedGuild(SocketGuild guild) => Task.CompletedTask;

		public virtual Task DiscordClientOnLeftGuild(SocketGuild guild) => Task.CompletedTask;

		public virtual Task DiscordClientOnMessageUpdated(SocketMessage originalMessage, SocketMessage newMessage, ISocketMessageChannel socketMessageChannel) => Task.CompletedTask;

		public virtual Task DiscordClientOnMessageDeleted(SocketMessage message, IMessageChannel channel) => Task.CompletedTask;

		public virtual Task DiscordClientOnMessagesBulkDeleted(ReadOnlyCollection<SocketMessage> messages, ISocketMessageChannel channel) => Task.CompletedTask;

		public virtual Task DiscordClientOnMessageReceived(SocketMessage message) => Task.CompletedTask;

		public virtual Task DiscordClientOnUserBanned(SocketUser user, SocketGuild guild) => Task.CompletedTask;

		public virtual Task DiscordClientOnUserUnbanned(SocketUser user, SocketGuild guild) => Task.CompletedTask;

		public virtual Task DiscordClientOnInviteCreated(SocketInvite invite) => Task.CompletedTask;

		public virtual Task DiscordClientOnInviteDeleted(SocketGuildChannel oldInviteChannel, string oldInviteUrl) => Task.CompletedTask;

		public virtual async Task ModuleCommand(SocketSlashCommand command) => await command.RespondAsync("Module did not implement base command!", ephemeral: true);

		public virtual bool TryLoad(ulong guild, out string reason)
		{
			reason = String.Empty;
			return true;
		}

		public virtual List<Type> GetConfigKnownTypes() => new List<Type>();

		public virtual void OnLoad(ulong guild) { }

		public virtual void OnUnload(ulong guild) { }

		[AttributeUsage(AttributeTargets.Method)]
		[UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
		public class ModuleCommandAttribute : Attribute
		{
			public ModuleCommandAttribute(GuildPermission commandPermissionRequirements)
			{
				CommandPermissionRequirements = commandPermissionRequirements;
			}

			public ModuleCommandAttribute()
			{
				CommandPermissionRequirements = null;
			}

			public GuildPermission? CommandPermissionRequirements { get; init; }

			public bool GuildOnly { get; init; }
		}

		[AttributeUsage(AttributeTargets.Method)]
		public class CommandGuildOnlyAttribute : Attribute { }

		[AttributeUsage(AttributeTargets.Method)]
		public class CommandDmOnlyAttribute : Attribute { }
	}
}
