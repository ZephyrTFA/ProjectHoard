using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Serialization;

using Discord;
using Discord.WebSocket;

using Hoard2.Module.Builtin;
using Hoard2.Util;

namespace Hoard2.Module
{
	public enum ModuleLoadResult
	{
		AlreadyLoaded,
		NotFound,
		LoadFailed,
		LoadErrored,
		Loaded,
	}

	public static class ModuleHelper
	{
		internal static readonly Dictionary<string, Type> TypeMap = new Dictionary<string, Type>();
		internal static readonly Dictionary<Type, ModuleBase> InstanceMap = new Dictionary<Type, ModuleBase>();
		static readonly Dictionary<Type, uint> LoadCount = new Dictionary<Type, uint>();
		static readonly Dictionary<ulong, ICollection<Type>> LoadedMap = new Dictionary<ulong, ICollection<Type>>();
		internal static DirectoryInfo ModuleDataStorageDirectory = null!;

		public static readonly Type[] InnateModules =
		{
			typeof(ModuleManager),
			typeof(UserDataHelper),
		};

		static bool _doForAllWorking;

		public static bool IsModuleLoaded(ulong guild, Type moduleType)
		{
			if (!LoadedMap.TryGetValue(guild, out var map))
				return false;
			return map.Contains(moduleType);
		}

		public static bool TryGetModule<T>([NotNullWhen(true)] out T? value) where T : ModuleBase
		{
			try
			{
				value = (T)InstanceMap[typeof(T)];
				return true;
			}
			catch
			{
				value = null;
				return false;
			}
		}

		public static ModuleLoadResult TryLoadModule(ulong guild, string moduleName, out Exception? exception, out string? failReason)
		{
			failReason = String.Empty;
			exception = null;
			try
			{
				if (!TypeMap.TryGetValue(moduleName, out var moduleType))
					return ModuleLoadResult.NotFound;
				if (IsModuleLoaded(guild, moduleType))
					return ModuleLoadResult.AlreadyLoaded;

				if (!DoLoadModule(guild, moduleType, out failReason))
					return ModuleLoadResult.LoadFailed;
				return ModuleLoadResult.Loaded;
			}
			catch (Exception error)
			{
				exception = error;
				return ModuleLoadResult.LoadErrored;
			}
		}

		static bool DoLoadModule(ulong guild, Type moduleType, out string failReason)
		{
			var module = ConstructModule(moduleType);
			if (!module.TryLoad(guild, out failReason))
				return false;

			module.OnLoad(guild);
			CommandHelper.ParseModuleCommands(moduleType);
			if (!LoadedMap.ContainsKey(guild))
				LoadedMap[guild] = new List<Type>();
			LoadedMap[guild].Add(moduleType);
			if (LoadCount.ContainsKey(moduleType))
				LoadCount[moduleType]++;
			else
				LoadCount[moduleType] = 1;

			UpdateRestoreInformation();
			return true;
		}

		public static void UnloadModule(ulong guild, Type moduleType)
		{
			if (!IsModuleLoaded(guild, moduleType))
				return;
			DoUnloadModule(guild, moduleType);
		}

		static void DoUnloadModule(ulong guild, Type moduleType)
		{
			var instance = InstanceMap[moduleType];
			instance.OnUnload(guild);

			CommandHelper.DropModuleCommands(moduleType);
			LoadedMap[guild].Remove(moduleType);
			if (LoadedMap[guild].Count == 0)
				LoadedMap.Remove(guild);

			var loadCount = --LoadCount[moduleType];
			if (loadCount == 0)
			{
				InstanceMap.Remove(moduleType);
				LoadCount.Remove(moduleType);
			}

			UpdateRestoreInformation();
		}

		static ModuleBase ConstructModule(Type moduleType)
		{
			if (InstanceMap.TryGetValue(moduleType, out var module))
				return module;
			var constructor = moduleType.GetConstructor(new[] { typeof(string) })!;
			var moduleInstance = (ModuleBase)constructor.Invoke(new object?[]
			{
				ModuleDataStorageDirectory.CreateSubdirectory(moduleType.FullName!.GetNormalizedRepresentation()).FullName,
			});
			InstanceMap[moduleType] = moduleInstance;
			return moduleInstance;
		}

		public static void CacheAssembly(Assembly assembly)
		{
			foreach (var exportedType in assembly.GetExportedTypes())
				if (exportedType.IsAssignableTo(typeof(ModuleBase)))
					TypeMap[exportedType.GetNormalizedRepresentation()] = exportedType;
		}

		public static void WipeCache()
		{
			TypeMap.Clear();
		}
		
		static async Task DoForAll(ulong matchGuild, Func<ModuleBase, Task> action)
		{
			while (_doForAllWorking)
				await Task.Yield();
			_doForAllWorking = true;

			var tasks = new List<Task>();
			foreach (var (type, module) in InstanceMap)
			{
				if (matchGuild != 0 && !IsModuleLoaded(matchGuild, type))
					continue;
				tasks.Add(action(module));
			}
			while (tasks.Count != 0)
			{
				tasks = tasks.FindAll(task => !task.IsCompleted);
				await Task.Yield();
			}

			_doForAllWorking = false;
		}

		public static async Task DiscordClientOnMessageReceived(SocketMessage message)
		{
			await DoForAll(message.Channel.GetGuildId(), async module => await module.DiscordClientOnMessageReceived(message));
		}

		public static async Task DiscordClientOnMessageDeleted(Cacheable<IMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel)
		{
			if (!message.HasValue || message.Value is not SocketMessage socketMessage)
				return;
			var channelActual = await channel.GetOrDownloadAsync();
			await DoForAll(channelActual.GetGuildId(), async module => await module.DiscordClientOnMessageDeleted(socketMessage, channelActual));
		}

		public static async Task DiscordClientOnMessagesBulkDeleted(IReadOnlyCollection<Cacheable<IMessage, ulong>> messages, Cacheable<IMessageChannel, ulong> channel)
		{
			var messagesActual = messages.Where(message => message.HasValue).Select(message => (SocketMessage)message.Value).ToList().AsReadOnly();
			var channelActual = (ISocketMessageChannel)await channel.GetOrDownloadAsync();
			await DoForAll(channelActual.GetGuildId(), async module => await module.DiscordClientOnMessagesBulkDeleted(messagesActual, channelActual));
		}

		public static async Task DiscordClientOnMessageUpdated(Cacheable<IMessage, ulong> oldMessage, SocketMessage newMessage, ISocketMessageChannel channel)
		{
			if (!oldMessage.HasValue || oldMessage.Value is not SocketMessage socketMessage)
				return;
			await DoForAll(channel.GetGuildId(), async module => await module.DiscordClientOnMessageUpdated(socketMessage, newMessage, channel));
		}

		public static async Task DiscordClientOnUserJoined(SocketGuildUser user) => await DoForAll(user.Guild.Id, async module => await module.DiscordClientOnUserJoined(user));

		public static async Task DiscordClientOnUserLeft(SocketGuild guild, SocketUser user) => await DoForAll(guild.Id, async module => await module.DiscordClientOnUserLeft(guild, user));

		public static async Task DiscordClientOnUserUpdated(SocketUser oldUser, SocketUser newUser) => await DoForAll(0, async module => await module.DiscordClientOnUserUpdated(oldUser, newUser));

		public static async Task DiscordClientOnGuildMemberUpdated(Cacheable<SocketGuildUser, ulong> oldUser, SocketGuildUser newUser)
		{
			if (!oldUser.HasValue)
				return;
			await DoForAll(newUser.Guild.Id, async module => await module.DiscordClientOnGuildMemberUpdated(oldUser.Value, newUser));
		}

		public static async Task DiscordClientOnButtonExecuted(SocketMessageComponent button)
		{
			if (button.GuildId is null)
				throw new NotImplementedException("Buttons are only implemented for Guilds.");
			await DoForAll(button.GuildId!.Value, module =>
			{
				if (module.GetButtonId(button) is not { } buttonId)
					return Task.CompletedTask;
				_ = module.OnButton(buttonId, button);
				return Task.CompletedTask;
			});
		}

		public static async Task DiscordClientOnSelectMenuExecuted(SocketMessageComponent menu)
		{
			if (menu.GuildId is null)
				throw new NotImplementedException("Menus are only implemented for Guilds.");
			await DoForAll(menu.GuildId!.Value, module =>
			{
				if (module.GetMenuId(menu) is not { } menuId)
					return Task.CompletedTask;
				_ = module.OnMenu(menuId, menu);
				return Task.CompletedTask;
			});
		}

		public static async Task DiscordClientOnJoinedGuild(SocketGuild guild) => await DoForAll(0, async module => await module.DiscordClientOnJoinedGuild(guild));

		public static async Task DiscordClientOnLeftGuild(SocketGuild guild) => await DoForAll(0, async module => await module.DiscordClientOnLeftGuild(guild));

		public static async Task DiscordClientOnUserBanned(SocketUser user, SocketGuild guild) => await DoForAll(guild.Id, async module => await module.DiscordClientOnUserBanned(user, guild));

		public static async Task DiscordClientOnUserUnbanned(SocketUser user, SocketGuild guild) => await DoForAll(guild.Id, async module => await module.DiscordClientOnUserUnbanned(user, guild));

		public static async Task DiscordClientOnInviteCreated(SocketInvite invite) => await DoForAll(invite.Guild.Id, async module => await module.DiscordClientOnInviteCreated(invite));

		public static async Task DiscordClientOnInviteDeleted(SocketGuildChannel oldInviteChannel, string oldInviteUrl) => await DoForAll(oldInviteChannel.GetGuildId(), async module => await module.DiscordClientOnInviteDeleted(oldInviteChannel, oldInviteUrl));

		static Dictionary<ulong, string[]>? GetModulesToRestore()
		{
			var storePath = Path.Join(ModuleDataStorageDirectory.FullName, "module_persistence_map.xml");
			if (!File.Exists(storePath))
				return null;
			using var store = File.OpenRead(storePath);
			var deserializer = new DataContractSerializer(typeof(Dictionary<ulong, string[]>));
			return (Dictionary<ulong, string[]>)deserializer.ReadObject(store)!;
		}

		static void UpdateRestoreInformation()
		{
			var storePath = Path.Join(ModuleDataStorageDirectory.FullName, "module_persistence_map.xml");
			using var store = new MemoryStream();
			var serializer = new DataContractSerializer(typeof(Dictionary<ulong, string[]>));

			var writing = new Dictionary<ulong, string[]>();
			foreach (var (guild, moduleList) in LoadedMap)
				writing[guild] = moduleList.Select(module => module.GetNormalizedRepresentation()).ToArray();

			serializer.WriteObject(store, writing);
			File.WriteAllBytes(storePath, store.ToArray());
		}

		public static void RestoreGuildModules()
		{
			if (GetModulesToRestore() is not { } toRestore)
				return;
			foreach (var (guild, typeArray) in toRestore)
				foreach (var type in typeArray)
					DoLoadModule(guild, TypeMap[type], out _);
		}

		public static void AssertInnateModules()
		{
			var knownFailures = new List<Type>();
			foreach (var guild in HoardMain.DiscordClient.Guilds.Select(guild => guild.Id))
			{
				foreach (var module in InnateModules)
				{
					if (IsModuleLoaded(guild, module))
						continue;
					switch (TryLoadModule(guild, module.GetNormalizedRepresentation(), out var exception, out var failReason))
					{
						case ModuleLoadResult.NotFound:
							if (knownFailures.Contains(module))
								break;
							HoardMain.Logger.LogError("Failed to load innate module: {}\n\t- does not exist", module);
							knownFailures.Add(module);
							break;

						case ModuleLoadResult.LoadFailed:
							HoardMain.Logger.LogError("Failed to load innate module: {}\n\t- '{}'", module, failReason);
							break;

						case ModuleLoadResult.LoadErrored:
							HoardMain.Logger.LogError(exception, "Failed to load innate module: {}", module);
							break;

						case ModuleLoadResult.AlreadyLoaded:
						case ModuleLoadResult.Loaded:
						default:
							break;
					}
				}
			}
		}
	}
}
