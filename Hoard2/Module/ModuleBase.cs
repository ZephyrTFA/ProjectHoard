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
		string _configDirectory;
		public ModuleBase(string configPath)
		{
			_configDirectory = configPath;
			GlobalConfig = new ModuleConfig(Path.Join(_configDirectory, "0.xml"));
		}

		protected ModuleConfig GlobalConfig { get; init; }

		public ModuleConfig GuildConfig(ulong guild) => new ModuleConfig(Path.Join(_configDirectory, $"{guild}.xml"));

		public virtual Task DiscordClientOnSlashCommandExecuted(SocketSlashCommand socketSlashCommand) => Task.CompletedTask;

		public virtual Task DiscordClientOnUserLeft(SocketGuild socketGuild, SocketUser socketUser) => Task.CompletedTask;

		public virtual Task DiscordClientOnUserJoined(SocketGuildUser socketGuildUser) => Task.CompletedTask;

		public virtual Task DiscordClientOnMessageUpdated(Cacheable<IMessage, ulong> cacheable, SocketMessage socketMessage, ISocketMessageChannel socketMessageChannel) => Task.CompletedTask;

		public virtual Task DiscordClientOnMessageDeleted(IMessage message, IGuildChannel channel) => Task.CompletedTask;

		public virtual Task DiscordClientOnMessageReceived(IMessage message) => Task.CompletedTask;

		public virtual bool TryLoad(ulong guild, out string reason)
		{
			reason = String.Empty;
			return true;
		}

		public virtual bool TryUnload(ulong guild, out string reason)
		{
			reason = String.Empty;
			return true;
		}
	}

	[AttributeUsage(AttributeTargets.Method)]
	[UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
	public class ModuleCommandAttribute : Attribute
	{
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

		public string CommandName { get; init; }

		public string CommandDescription { get; init; }

		public GuildPermission CommandPermissionRequirements { get; init; }

		public string[] CommandParamNames { get; init; } = Array.Empty<string>();

		public Type[] CommandParamTypes { get; init; } = Array.Empty<Type>();

		public string[] CommandParamDescriptions { get; init; } = Array.Empty<string>();
	}
}
