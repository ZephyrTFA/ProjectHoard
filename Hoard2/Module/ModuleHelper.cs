using System.Reflection;

using Discord;

namespace Hoard2.Module
{
	public static class ModuleHelper
	{
		public static Dictionary<string, Type> Modules = new Dictionary<string, Type>();
		public static void LoadAssembly(Assembly assembly, out List<string> errors)
		{
			errors = new List<string>();
			foreach (var type in assembly.ExportedTypes)
			{
				if (type.BaseType == typeof(ModuleBase))
				{
					var moduleId = type.Name.ToLower();
					if (Modules.ContainsKey(moduleId))
					{
						errors.Add($"Conflicting module {moduleId} in assembly ({assembly.FullName}){assembly.Location}");
						continue;
					}

					Modules[moduleId] = type;
				}
			}

			HoardMain.DiscordClient.SetGameAsync($"{Modules.Count} modules", type: ActivityType.Watching).Wait();
		}
	}
}
