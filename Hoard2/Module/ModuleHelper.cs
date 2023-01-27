using System.Reflection;
using System.Runtime.Serialization;

using Discord;
using Discord.WebSocket;

namespace Hoard2.Module
{
	public static class ModuleHelper
	{
		public static readonly Dictionary<string, Type> ModuleTypes = new Dictionary<string, Type>();
		public static readonly Dictionary<string, ModuleBase> ModuleInstances = new Dictionary<string, ModuleBase>();
		public static readonly Dictionary<ulong, List<string>> GuildModules = new Dictionary<ulong, List<string>>();
		public static readonly Dictionary<string, uint> ModuleUsageCount = new Dictionary<string, uint>();

		public static IReadOnlyList<string> SystemModules = new[]
		{
			"ModuleManager",
		};

		public static void LoadAssembly(Assembly assembly, out List<string> errors)
		{
			errors = new List<string>();
			foreach (var type in assembly.ExportedTypes)
			{
				if (type.BaseType == typeof(ModuleBase))
				{
					var moduleId = type.Name.ToLower();
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

		public static bool UnloadModule(ulong guild, string module, out string failReason)
		{
			failReason = String.Empty;
			module = module.ToLower().Trim();

			if (!GuildModules.ContainsKey(guild)) GuildModules[guild] = new List<string>();
			var guildModules = GuildModules[guild];

			if (!guildModules.Contains(module))
			{
				failReason = "module not loaded";
				return false;
			}

			if (SystemModules.Any(entry => entry.ToLower().Trim().Equals(module)))
			{
				failReason = "cannot unload a system module";
				return false;
			}

			var instance = ModuleInstances[module];
			if (!instance.TryUnload(guild, out failReason)) return false;
			CommandHelper.ClearModuleCommands(guild, instance);
			guildModules.Remove(module);
			ModuleUsageCount[module] -= 1;

			if (ModuleUsageCount[module] == 0)
				HandleModuleDestroy(instance);
			SaveLoadedModules();
			return true;
		}

		public static void HandleModuleDestroy(ModuleBase module)
		{
			var moduleName = module.GetType().Name.ToLower().Trim();
			ModuleInstances.Remove(moduleName);
			ModuleUsageCount.Remove(moduleName);
		}

		public static bool LoadModule(ulong guild, string module, out string failReason)
		{
			failReason = String.Empty;
			module = module.ToLower().Trim();

			if (!GuildModules.ContainsKey(guild)) GuildModules[guild] = new List<string>();
			var guildModules = GuildModules[guild];

			if (guildModules.Contains(module))
			{
				failReason = "module already loaded";
				return false;
			}

			if (!ModuleTypes.ContainsKey(module))
			{
				failReason = "module does not exist";
				return false;
			}

			try
			{
				// if an instance doesn't exist, create one
				if (!ModuleInstances.TryGetValue(module, out var instance))
				{
					HoardMain.Logger.LogInformation("Creating module {}", module);
					ModuleUsageCount[module] = 0;
					if (ModuleTypes[module].GetConstructor(new[] { typeof(string) })?.Invoke(new object?[] { Path.Join(HoardMain.DataDirectory.CreateSubdirectory("config").FullName, $"{module}.xml") }) is not ModuleBase newInstance)
					{
						failReason = "failed to find or invoke module constructor";
						return false;
					}

					if (!CommandHelper.RefreshModuleCommands(guild, newInstance, out failReason)) return false;
					ModuleInstances[module] = instance = newInstance;
				}

				if (!instance.TryLoad(guild, out failReason)) return false;
			}
			catch (Exception e)
			{
				failReason = $"exception during module load: {e}";
				return false;
			}

			ModuleUsageCount[module] += 1;
			guildModules.Add(module);
			SaveLoadedModules();
			return true;
		}

		static void SaveLoadedModules()
		{
			var store = Path.Join(HoardMain.DataDirectory.FullName, "loaded.xml");
			if (File.Exists(store)) File.Delete(store);
			using var writer = File.OpenWrite(store);
			new DataContractSerializer(typeof(Dictionary<ulong, List<string>>)).WriteObject(writer, GuildModules);
			writer.Dispose();
		}

		public static void RestoreModules()
		{
			HoardMain.Logger.LogInformation("Restoring Modules");
			foreach (var guild in HoardMain.DiscordClient.Guilds)
				foreach (var systemModule in SystemModules)
					if (!LoadModule(guild.Id, systemModule, out var reason))
						HoardMain.Logger.LogCritical("Failed to restore system module {}: {}", systemModule, reason);

			foreach (var (guild, modules) in CheckLoadedModules())
				foreach (var module in modules.Where(module => !SystemModules.Any(entry => entry.ToLower().Trim().Equals(module))))
					if (!LoadModule(guild, module, out var reason))
						HoardMain.Logger.LogCritical("Failed to restore module {}: {}", module, reason);
		}

		static Dictionary<ulong, List<string>> CheckLoadedModules()
		{
			var store = Path.Join(HoardMain.DataDirectory.FullName, "loaded.xml");
			if (!File.Exists(store))
				return new Dictionary<ulong, List<string>>();
			using var reader = File.OpenRead(store);
			return new DataContractSerializer(typeof(Dictionary<ulong, List<string>>)).ReadObject(reader) as Dictionary<ulong, List<string>>
				?? new Dictionary<ulong, List<string>>();
		}

		internal static async Task DiscordClientOnUserLeft(SocketGuild arg1, SocketUser arg2)
		{
			if (!GuildModules.TryGetValue(arg1.Id, out var modules)) return;
			foreach (var module in modules.Select(moduleID => ModuleInstances[moduleID]))
				await module.DiscordClientOnUserLeft(arg1, arg2);
		}

		internal static async Task DiscordClientOnUserJoined(SocketGuildUser socketGuildUser)
		{
			if (!GuildModules.TryGetValue(socketGuildUser.Guild.Id, out var modules)) return;
			foreach (var module in modules.Select(moduleID => ModuleInstances[moduleID]))
				await module.DiscordClientOnUserJoined(socketGuildUser);
		}

		internal static async Task DiscordClientOnMessageUpdated(Cacheable<IMessage, ulong> cacheableMessage, SocketMessage socketMessage, ISocketMessageChannel socketMessageChannel)
		{
			if (socketMessageChannel is not IGuildChannel guildChannel) return;
			if (!GuildModules.TryGetValue(guildChannel.GuildId, out var modules)) return;
			foreach (var module in modules.Select(moduleID => ModuleInstances[moduleID]))
				await module.DiscordClientOnMessageUpdated(cacheableMessage, socketMessage, socketMessageChannel);
		}

		internal static async Task DiscordClientOnMessageDeleted(Cacheable<IMessage, ulong> cacheable, Cacheable<IMessageChannel, ulong> cacheableChannel)
		{
			if (!cacheable.HasValue || cacheableChannel.HasValue || cacheableChannel.Value is not IGuildChannel guildChannel) return;
			if (!GuildModules.TryGetValue(guildChannel.GuildId, out var modules)) return;
			foreach (var module in modules.Select(moduleID => ModuleInstances[moduleID]))
				await module.DiscordClientOnMessageDeleted(cacheable.Value, guildChannel);
		}

		internal static async Task DiscordClientOnMessageReceived(IMessage message)
		{
			// why is this needed?
			await Task.Yield();
			message = await message.Channel.GetMessageAsync(message.Id);

			if (message.Channel is not IGuildChannel guildChannel) return;

			if (await HoardMain.HandleSystemCommand(message)) return;
			if (!GuildModules.TryGetValue(guildChannel.GuildId, out var modules)) return;
			foreach (var module in modules.Select(moduleID => ModuleInstances[moduleID]))
				await module.DiscordClientOnMessageReceived(message);
		}
	}
}
