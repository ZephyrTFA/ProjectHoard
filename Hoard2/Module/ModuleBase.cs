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

		List<Guid> _knownButtons = new List<Guid>();

		List<Guid> _knownMenus = new List<Guid>();
		public ModuleBase(string configPath)
		{
			_configDirectory = configPath;
			GlobalConfig = new ModuleConfig(Path.Join(_configDirectory, "0.xml"));
		}

		protected ModuleConfig GlobalConfig { get; init; }

		public string GetModuleName() => GetType().Name.MTrim();

		public ModuleConfig GuildConfig(ulong guild) => new ModuleConfig(Path.Join(_configDirectory, $"{guild}.xml"));

		public ModuleConfig CustomConfig(string key) => new ModuleConfig(Path.Join(_configDirectory, "custom", key));

		public virtual Task DiscordClientOnSlashCommandExecuted(SocketSlashCommand socketSlashCommand) => Task.CompletedTask;

		public virtual Task DiscordClientOnUserLeft(SocketGuild socketGuild, SocketUser socketUser) => Task.CompletedTask;

		public virtual Task DiscordClientOnUserJoined(SocketGuildUser socketGuildUser) => Task.CompletedTask;

		public virtual Task DiscordClientOnUserUpdated(SocketGuildUser originalUser, SocketGuildUser newUser) => Task.CompletedTask;

		public virtual Task DiscordClientOnMessageUpdated(IMessage originalMessage, SocketMessage newMessage, IGuildChannel socketMessageChannel) => Task.CompletedTask;

		public virtual Task DiscordClientOnMessageDeleted(IMessage message, IGuildChannel channel) => Task.CompletedTask;

		public virtual Task DiscordClientOnMessageReceived(IMessage message) => Task.CompletedTask;

		public virtual async Task ModuleCommand(SocketSlashCommand command) => await command.RespondAsync("Module did not implement base command!", ephemeral: true);

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

		public virtual void OnLoad(ulong guild) { }

		public virtual void OnUnload(ulong guild) { }

		public virtual Task OnButton(SocketMessageComponent button, string buttonId) => Task.CompletedTask;

		public virtual Task OnMenu(SocketMessageComponent menu, string menuId) => Task.CompletedTask;

		public ButtonBuilder GetButton(string buttonID, ulong guild)
		{
			Guid id;
			do { id = Guid.NewGuid(); }
			while (_knownButtons.Contains(id));
			_knownButtons.Add(id);
			return new ButtonBuilder().WithCustomId($"h/{GetModuleName()}/{id}/{buttonID}");
		}
		public SelectMenuBuilder GetMenu(string menuId, ulong guild)
		{
			Guid id;
			do { id = Guid.NewGuid(); }
			while (_knownMenus.Contains(id));
			_knownMenus.Add(id);
			return new SelectMenuBuilder().WithCustomId($"h/{GetModuleName()}/{id}/{menuId}");
		}

		public bool CheckButton(Guid button) => _knownButtons.Contains(button);

		public bool CheckMenu(Guid menu) => _knownMenus.Contains(menu);
	}

	[AttributeUsage(AttributeTargets.Method)]
	[UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
	public class ModuleCommandAttribute : Attribute
	{
		public ModuleCommandAttribute(string commandName,
																	string commandDescription,
																	GuildPermission commandPermissionRequirements)
		{
			CommandName = commandName.ToLower();
			CommandDescription = commandDescription;
			CommandPermissionRequirements = commandPermissionRequirements;
		}

		public ModuleCommandAttribute(string commandDescription)
		{
			CommandDescription = commandDescription;
		}

		public ModuleCommandAttribute(string commandDescription, GuildPermission commandPermissionRequirements) : this(commandDescription)
		{
			CommandPermissionRequirements = commandPermissionRequirements;
		}

		public ModuleCommandAttribute(string commandName,
																	string commandDescription)
		{
			CommandName = commandName;
			CommandDescription = commandDescription;
		}

		public string? CommandName { get; init; }

		public string CommandDescription { get; init; }

		public GuildPermission? CommandPermissionRequirements { get; init; }
	}
}
