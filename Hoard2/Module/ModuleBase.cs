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

		public ModuleConfig CustomConfig(string key) => new ModuleConfig(Path.Join(_configDirectory, "custom", key));

		public virtual Task DiscordClientOnSlashCommandExecuted(SocketSlashCommand socketSlashCommand) => Task.CompletedTask;

		public virtual Task DiscordClientOnUserLeft(SocketGuild socketGuild, SocketUser socketUser) => Task.CompletedTask;

		public virtual Task DiscordClientOnUserJoined(SocketGuildUser socketGuildUser) => Task.CompletedTask;

		public virtual Task DiscordClientOnUserUpdated(SocketGuildUser current, SocketGuildUser old) => Task.CompletedTask;

		public virtual Task DiscordClientOnMessageUpdated(IMessage originalMessage, SocketMessage newMessage, IGuildChannel socketMessageChannel) => Task.CompletedTask;

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

		protected Task CreateTimer(ulong guild, TimeSpan interval, Func<Task> callback, uint times = 0)
		{
			async Task Action()
			{
				var count = 0;
				while (!HoardMain.HoardToken.IsCancellationRequested && (times == 0 || count < times))
				{
					await Task.Delay(interval);

					if (HoardMain.HoardToken.IsCancellationRequested)
						return;
					var myType = GetType();
					var getLoaded = typeof(ModuleHelper).GetMethod(nameof(ModuleHelper.GetModuleInstance))?.MakeGenericMethod(myType)!;
					if (getLoaded.Invoke(null, new object?[]{ guild }) != this)
						return;
					await callback.Invoke();
					count++;
				}
			}
			return Task.Run(Action);
		}
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

		public ModuleCommandAttribute(string commandName,
																	string commandDescription)
		{
			CommandName = commandName;
			CommandDescription = commandDescription;
		}

		public string CommandName { get; init; }

		public string CommandDescription { get; init; }

		public GuildPermission? CommandPermissionRequirements { get; init; }
	}
}
