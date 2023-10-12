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
		private string _configDirectory;

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

		public virtual Task DiscordClientOnGuildMemberUpdated(SocketGuildUser oldUserValue, SocketGuildUser newUser) => Task.CompletedTask;

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

		private Dictionary<string, List<(Guid, ulong?)>> _knownMenus = new Dictionary<string, List<(Guid, ulong?)>>();
		public SelectMenuBuilder CreateMenu(string menuId, ulong? user = null)
		{
			if (!_knownMenus.ContainsKey(menuId))
				_knownMenus[menuId] = new List<(Guid, ulong?)>();

			Guid menuGuid;
			do
				menuGuid = Guid.NewGuid();
			while (_knownMenus[menuId].Any(guidPair => guidPair.Item1.Equals(menuGuid)));
			_knownMenus[menuId].Add((menuGuid, user));

			return new SelectMenuBuilder().WithCustomId($"{menuGuid.ToString()}/{menuId}");
		}

		public string? GetMenuId(SocketMessageComponent menu)
		{
			if (String.IsNullOrWhiteSpace(menu.Data.CustomId))
				return null;

			var menuId = menu.Data.CustomId;
			var firstSlash = menuId.IndexOf('/');
			if (firstSlash is -1)
				return null;

			var guidString = menuId[..firstSlash];
			if (!Guid.TryParse(guidString, out var guidActual))
				return null;

			var menuIdActual = menuId[(firstSlash + 1)..];
			if (!_knownMenus.TryGetValue(menuIdActual, out var knownIds))
				return null;

			(Guid, ulong?)? match = null;
			foreach (var knownId in knownIds)
				if (knownId.Item1.Equals(guidActual))
				{
					match = knownId;
					break;
				}
			if (match is null)
				return null;
			if (match.Value.Item2 is { } && match.Value.Item2.Value != menu.User.Id)
				return null;
			return menuIdActual;
		}

		private Dictionary<string, List<(Guid, ulong?)>> _knownButtons = new Dictionary<string, List<(Guid, ulong?)>>();
		public ButtonBuilder CreateButton(string buttonId, ulong? user = null)
		{
			if (!_knownButtons.ContainsKey(buttonId))
				_knownButtons[buttonId] = new List<(Guid, ulong?)>();

			Guid buttonGuid;
			do
				buttonGuid = Guid.NewGuid();
			while (_knownButtons[buttonId].Any(guidPair => guidPair.Item1.Equals(buttonGuid)));
			_knownButtons[buttonId].Add((buttonGuid, user));

			return new ButtonBuilder().WithCustomId($"{buttonGuid.ToString("")}/{buttonId}");
		}

		public string? GetButtonId(SocketMessageComponent button)
		{
			if (String.IsNullOrWhiteSpace(button.Data.CustomId))
				return null;

			var buttonId = button.Data.CustomId;
			var firstSlash = buttonId.IndexOf('/');
			if (firstSlash is -1)
				return null;

			var guidString = buttonId[..firstSlash];
			if (!Guid.TryParse(guidString, out var guidActual))
				return null;

			var buttonIdActual = buttonId[(firstSlash + 1)..];
			if (!_knownButtons.TryGetValue(buttonIdActual, out var knownIds))
				return null;

			(Guid, ulong?)? match = null;
			foreach (var knownId in knownIds)
				if (knownId.Item1.Equals(guidActual))
				{
					match = knownId;
					break;
				}
			if (match is null)
				return null;
			if (match.Value.Item2 is { } && match.Value.Item2.Value != button.User.Id)
				return null;
			return buttonIdActual;
		}

		public virtual Task OnButton(string buttonId, SocketMessageComponent button) => Task.CompletedTask;

		public virtual Task OnMenu(string menuId, SocketMessageComponent menu) => Task.CompletedTask;

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
