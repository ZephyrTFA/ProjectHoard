using System.Collections.Immutable;
using System.ComponentModel;
using System.Reflection;

using Discord;

using Hoard2.Module;

namespace Hoard2.Util
{
	public class ParameterInformation
	{
		public readonly object? Default;
		public readonly string Desc;

		public readonly string Name;
		public readonly bool Required;
		public readonly ApplicationCommandOptionType Type;
		public ParameterInformation(string name, string desc, ApplicationCommandOptionType type, object? @default, bool required)
		{
			Name = name;
			Desc = desc;
			Type = type;
			Default = @default;
			Required = required;
		}

		public SlashCommandOptionBuilder IntoBuilder()
		{
			var builder = new SlashCommandOptionBuilder()
				.WithName(Name)
				.WithType(Type)
				.WithRequired(Default is null or DBNull && Required)
				.WithDescription(Desc);
			return builder;
		}

		public static ParameterInformation GenerateParameterInformation(ParameterInfo parameterInfo)
		{
			var name = parameterInfo.Name!.GetNormalizedRepresentation();
			var desc = parameterInfo.GetCustomAttribute<DescriptionAttribute>() is { } descriptionAttribute ?
				descriptionAttribute.Description : "No description provided.";

			return new ParameterInformation(
				name,
				desc,
				parameterInfo.ParameterType.ToDiscordCommandType(),
				parameterInfo.DefaultValue,
				!parameterInfo.Attributes.HasFlag(ParameterAttributes.Optional)
			);
		}
	}

	public class ModuleCommandInformation
	{
		public readonly MethodInfo Caller;
		public readonly string Desc;
		public readonly bool GuildOnly, DmOnly;

		public readonly string Name;
		public readonly ImmutableList<ParameterInformation> Parameters;
		public readonly GuildPermission? Permission;

		private ModuleCommandInformation(string name, string desc, ImmutableList<ParameterInformation> parameters, MethodInfo caller, GuildPermission? permissions, bool guildOnly, bool dmOnly)
		{
			Name = name;
			Desc = desc;
			Parameters = parameters;
			Caller = caller;
			Permission = permissions;
			GuildOnly = guildOnly;
			DmOnly = dmOnly;
		}

		public SlashCommandOptionBuilder IntoBuilder()
		{
			var builder = new SlashCommandOptionBuilder()
				.WithName(Name)
				.WithType(ApplicationCommandOptionType.SubCommand)
				.WithDescription(Desc);
			foreach (var parameter in Parameters)
				builder.AddOption(parameter.IntoBuilder());
			return builder;
		}

		public static ModuleCommandInformation GenerateCommandInformation(MethodInfo command)
		{
			var name = command.Name.GetNormalizedRepresentation();
			var desc = command.GetCustomAttribute<DescriptionAttribute>() is { } descriptionAttribute ?
				descriptionAttribute.Description : "No description provided.";

			var permissions = command.GetCustomAttribute<ModuleBase.ModuleCommandAttribute>()!.CommandPermissionRequirements;
			var parameters = command.GetParameters().Skip(1).ToList();
			var paramInfo = parameters.Select(ParameterInformation.GenerateParameterInformation).ToImmutableList();

			var guildOnly = command.GetCustomAttribute<ModuleBase.CommandGuildOnlyAttribute>() is not null;
			var dmOnly = command.GetCustomAttribute<ModuleBase.CommandDmOnlyAttribute>() is not null;

			return new ModuleCommandInformation(name, desc, paramInfo, command, permissions, guildOnly, dmOnly);
		}
	}

	public class ModuleCommandMap
	{
		public readonly ImmutableList<ModuleCommandInformation> Commands;

		public readonly Type Module;

		private ModuleCommandMap(Type module, ImmutableList<ModuleCommandInformation> commands)
		{
			Module = module;
			Commands = commands;
		}

		public SlashCommandOptionBuilder IntoBuilder()
		{
			var builder = new SlashCommandOptionBuilder()
				.WithName(Module.GetNormalizedRepresentation())
				.WithType(ApplicationCommandOptionType.SubCommandGroup)
				.WithDescription("Commands for module.");
			foreach (var command in Commands)
				builder.AddOption(command.IntoBuilder());
			return builder;
		}

		public static ModuleCommandMap GenerateCommandMap(Type moduleType)
		{
			var commandMethods = moduleType.GetMethods().Where(method => method.GetCustomAttribute<ModuleBase.ModuleCommandAttribute>() is not null).ToList();
			commandMethods.Sort((left, right) => String.Compare(left.Name, right.Name, StringComparison.Ordinal));
			var commands = commandMethods.Select(ModuleCommandInformation.GenerateCommandInformation).ToImmutableList();
			return new ModuleCommandMap(moduleType, commands);
		}
	}
}
