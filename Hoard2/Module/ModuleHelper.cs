using System.Reflection;
using System.Runtime.Serialization;

using Discord;
using Discord.WebSocket;

using Hoard2.Module.Builtin;

namespace Hoard2.Module
{
	public static class ModuleHelper
	{
		public static readonly Dictionary<string, Type> ModuleTypes = new Dictionary<string, Type>();
		public static readonly Dictionary<string, ModuleBase> ModuleInstances = new Dictionary<string, ModuleBase>();
		public static readonly Dictionary<ulong, List<string>> GuildModules = new Dictionary<ulong, List<string>>();
		public static readonly Dictionary<string, uint> ModuleUsageCount = new Dictionary<string, uint>();

		public static readonly IReadOnlyList<Type> SystemModules = new[]
		{
			typeof(ModuleManager),
			typeof(UserDataHelper),
		};

		static bool _restoring;

		public static T? GetModuleInstance<T>(ulong guild) where T : ModuleBase
		{
			// Verify that guild has the module loaded!
			var moduleId = typeof(T).Name.MTrim();
			if (!GuildModules.TryGetValue(guild, out var loadedModules)) return null;
			if (!loadedModules.Contains(moduleId)) return null;
			// okay, its loaded, now return the global instance
			return (T?)ModuleInstances.Select(kvp => kvp.Value).FirstOrDefault(module => module.GetType() == typeof(T));
		}

		public static bool IsModuleLoaded(ulong guild, ModuleBase module)
		{
			var moduleId = module.GetModuleName();
			if (!GuildModules.ContainsKey(guild)) return false;
			return GuildModules[guild].Contains(moduleId);
		}

		public static void LoadAssembly(Assembly assembly, out List<string> errors)
		{
			errors = new List<string>();
			foreach (var type in assembly.ExportedTypes)
			{
				if (type.BaseType == typeof(ModuleBase))
				{
					var moduleId = type.Name.MTrim();
					if (ModuleTypes.ContainsKey(moduleId))
					{
						errors.Add($"Conflicting module {moduleId} in assembly ({assembly.FullName}){assembly.Location}");
						continue;
					}

					ModuleTypes[moduleId] = type;
				}
			}

			HoardMain.DiscordClient.SetGameAsync($"{ModuleTypes.Count} modules", type: ActivityType.Watching).Wait();
		}

		public static async Task UnloadModule(ulong guild, string module)
		{
			HoardMain.Logger.LogInformation("Trying to unload module '{}' for guild '{}'", module, guild);
			module = module.MTrim();

			if (!GuildModules.ContainsKey(guild)) GuildModules[guild] = new List<string>();
			var guildModules = GuildModules[guild];

			if (!guildModules.Contains(module))
				throw new Exception("module not loaded");
			if (SystemModules.Any(entry => entry.Name.MTrim().Equals(module)))
				throw new Exception("cannot unload a system module");

			var instance = ModuleInstances[module];
			if (!instance.TryUnload(guild, out var failReason))
				throw new Exception($"Failed to unload module: {failReason}");

			instance.OnUnload(guild);
			await CommandHelper.WipeGuildModuleCommand(guild, instance);
			guildModules.Remove(module);
			ModuleUsageCount[module] -= 1;

			if (ModuleUsageCount[module] == 0)
				HandleModuleDestroy(instance);
			SaveLoadedModules();
			HoardMain.Logger.LogInformation("Unload complete");
		}

		public static void HandleModuleDestroy(ModuleBase module)
		{
			HoardMain.Logger.LogInformation("Module({}) no longer loaded, destroying", module.GetType().Name);
			var moduleName = module.GetType().Name.ToLower().Trim();
			ModuleInstances.Remove(moduleName);
			ModuleUsageCount.Remove(moduleName);
		}

		public static async Task LoadModule(ulong guild, string module)
		{
			HoardMain.Logger.LogInformation("Trying to load module '{}' for guild '{}'", module, guild);
			module = module.MTrim();

			if (!GuildModules.ContainsKey(guild)) GuildModules[guild] = new List<string>();
			var guildModules = GuildModules[guild];

			if (guildModules.Contains(module))
				throw new Exception("Module is already loaded");

			if (!ModuleTypes.ContainsKey(module))
				throw new Exception("Module does not exist");

			// if an instance doesn't exist, create one
			if (!ModuleInstances.TryGetValue(module, out var instance))
			{
				HoardMain.Logger.LogInformation("Creating module {}", module);
				ModuleUsageCount[module] = 0;
				if (ModuleTypes[module]
						.GetConstructor(new[] { typeof(string) })?
						.Invoke(new object?[] { HoardMain.DataDirectory.CreateSubdirectory("config").CreateSubdirectory(module).FullName })
					is not ModuleBase newInstance)
					throw new Exception("Failed to find or invoke module constructor");

				ModuleInstances[module] = instance = newInstance;
			}

			if (!instance.TryLoad(guild, out var failReason))
				throw new Exception($"Module failed to load: {failReason}");
			await CommandHelper.UpdateGuildModuleCommand(guild, instance);
			instance.OnLoad(guild);

			ModuleUsageCount[module] += 1;
			guildModules.Add(module);
			SaveLoadedModules();
		}

		static void SaveLoadedModules()
		{
			if (_restoring) return;
			var store = Path.Join(HoardMain.DataDirectory.FullName, "loaded.xml");
			if (File.Exists(store)) File.Delete(store);
			using var writer = File.OpenWrite(store);
			new DataContractSerializer(typeof(Dictionary<ulong, List<string>>)).WriteObject(writer, GuildModules);
			writer.Dispose();
		}

		public static async Task RestoreModules()
		{
			if (_restoring) return; // already restoring

			_restoring = true;

			ModuleTypes.Clear();
			LoadAssembly(Assembly.GetExecutingAssembly(), out _);

			HoardMain.Logger.LogInformation("Restoring Modules");
			foreach (var guild in HoardMain.DiscordClient.Guilds)
				foreach (var systemModule in SystemModules)
					try { await LoadModule(guild.Id, systemModule.Name.MTrim()); }
					catch (Exception e) { HoardMain.Logger.LogCritical("Failed to restore system module {}: {}", systemModule, e); }

			foreach (var (guild, modules) in CheckLoadedModules())
				foreach (var module in modules.Where(module => !SystemModules.Any(entry => entry.Name.MTrim().Equals(module))))
					try { await LoadModule(guild, module); }
					catch (Exception e) { HoardMain.Logger.LogCritical("Failed to restore system module {}: {}", module, e); }
			_restoring = false;
		}

		static Dictionary<ulong, List<string>> CheckLoadedModules()
		{
			var store = Path.Join(HoardMain.DataDirectory.FullName, "loaded.xml");
			if (!File.Exists(store))
				return new Dictionary<ulong, List<string>>();
			using var reader = File.OpenRead(store);
			return (new DataContractSerializer(typeof(Dictionary<ulong, List<string>>)).ReadObject(reader) as Dictionary<ulong, List<string>>)!;
		}

		internal static async Task DiscordClientOnUserLeft(SocketGuild arg1, SocketUser arg2)
		{
			if (HoardMain.HoardToken.IsCancellationRequested) return;
			if (!GuildModules.TryGetValue(arg1.Id, out var modules)) return;
			foreach (var module in modules.Select(moduleID => ModuleInstances[moduleID]))
				await module.DiscordClientOnUserLeft(arg1, arg2);
		}

		internal static async Task DiscordClientOnUserJoined(SocketGuildUser socketGuildUser)
		{
			if (HoardMain.HoardToken.IsCancellationRequested) return;
			if (!GuildModules.TryGetValue(socketGuildUser.Guild.Id, out var modules)) return;
			foreach (var module in modules.Select(moduleID => ModuleInstances[moduleID]))
				await module.DiscordClientOnUserJoined(socketGuildUser);
		}

		internal static async Task DiscordClientOnUserUpdated(SocketUser arg1, SocketUser arg2)
		{
			if (HoardMain.HoardToken.IsCancellationRequested) return;
			if (!GuildModules.TryGetValue(arg1.Id, out var modules)) return;
			foreach (var module in modules.Select(moduleID => ModuleInstances[moduleID]))
				await module.DiscordClientOnUserUpdated((SocketGuildUser)arg1, (SocketGuildUser)arg2);
		}

		internal static async Task DiscordClientOnMessageUpdated(Cacheable<IMessage, ulong> cacheableMessage, SocketMessage socketMessage, ISocketMessageChannel socketMessageChannel)
		{
			if (HoardMain.HoardToken.IsCancellationRequested) return;
			if (socketMessageChannel is not IGuildChannel guildChannel) return;
			if (!cacheableMessage.HasValue) return;
			if (cacheableMessage.Value.Author.IsBot) return;
			if (!GuildModules.TryGetValue(guildChannel.GuildId, out var modules)) return;
			foreach (var module in modules.Select(moduleID => ModuleInstances[moduleID]))
				await module.DiscordClientOnMessageUpdated(cacheableMessage.Value, socketMessage, guildChannel);
		}

		internal static async Task DiscordClientOnMessageDeleted(Cacheable<IMessage, ulong> cacheable, Cacheable<IMessageChannel, ulong> cacheableChannel)
		{
			if (HoardMain.HoardToken.IsCancellationRequested) return;
			if (!cacheable.HasValue || !cacheableChannel.HasValue || cacheableChannel.Value is not IGuildChannel guildChannel) return;
			if (cacheable.Value.Author.IsBot) return;
			if (!GuildModules.TryGetValue(guildChannel.GuildId, out var modules)) return;
			foreach (var module in modules.Select(moduleID => ModuleInstances[moduleID]))
				await module.DiscordClientOnMessageDeleted(cacheable.Value, guildChannel);
		}

		internal static async Task DiscordClientOnMessagesBulkDeleted(IReadOnlyCollection<Cacheable<IMessage, ulong>> cacheableMessages, Cacheable<IMessageChannel, ulong> cacheableChannel)
		{
			if (HoardMain.HoardToken.IsCancellationRequested) return;
			if (!cacheableChannel.HasValue || cacheableChannel.Value is not IGuildChannel guildChannel) return;
			if (!GuildModules.TryGetValue(guildChannel.GuildId, out var modules)) return;
			foreach (var module in modules.Select(moduleID => ModuleInstances[moduleID]))
				foreach (var cacheableMessage in cacheableMessages
					.Where(cacheableMessage => cacheableMessage.HasValue && !cacheableMessage.Value.Author.IsBot)
					.Select(cacheableMessage => cacheableMessage.Value))
					await module.DiscordClientOnMessageDeleted(cacheableMessage, guildChannel);
		}

		internal static async Task DiscordClientOnMessageReceived(IMessage message)
		{
			if (HoardMain.HoardToken.IsCancellationRequested) return;
			// why is this needed?
			await Task.Yield();
			message = await message.Channel.GetMessageAsync(message.Id);

			if (message.Channel is not IGuildChannel guildChannel) return;
			if (message.Author.IsBot) return;
			if (!GuildModules.TryGetValue(guildChannel.GuildId, out var modules)) return;
			foreach (var module in modules.Select(moduleID => ModuleInstances[moduleID]))
				await module.DiscordClientOnMessageReceived(message);
		}

		internal static async Task JoinedGuild(SocketGuild guild)
		{
			if (HoardMain.HoardToken.IsCancellationRequested) return;
			await guild.DeleteApplicationCommandsAsync();
			foreach (var systemModule in SystemModules)
				try { await LoadModule(guild.Id, systemModule.Name.MTrim()); }
				catch (Exception e) { HoardMain.Logger.LogCritical("Failed to restore system module {}: {}", systemModule, e); }
		}

		internal static Task LeftGuild(SocketGuild guild)
		{
			if (HoardMain.HoardToken.IsCancellationRequested) return Task.CompletedTask;
			if (!GuildModules.TryGetValue(guild.Id, out _)) return Task.CompletedTask;
			GuildModules.Remove(guild.Id);
			SaveLoadedModules();
			return Task.CompletedTask;
		}
	}
}
