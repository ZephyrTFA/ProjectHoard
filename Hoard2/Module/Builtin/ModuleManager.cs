﻿using System.Text;
using Discord;
using Discord.WebSocket;
using Hoard2.Util;

namespace Hoard2.Module.Builtin;

public class ModuleManager : ModuleBase
{
    public ModuleManager(string configPath) : base(configPath)
    {
    }

    [ModuleCommand(GuildPermission.Administrator)]
    [CommandGuildOnly]
    public static async Task ListModules(SocketSlashCommand command, string? filter = null)
    {
        var message = new StringBuilder($"{(filter is null ? "All" : " filtered")} modules:\n```diff\n");
        var filtered = ModuleHelper.TypeMap.Where(kvp =>
        {
            if (filter is null)
                return true;
            return kvp.Key.Contains(filter);
        }).OrderBy(kvp => kvp.Key).ToList();

        if (!filtered.Any())
        {
            await command.RespondAsync("No modules found.");
            return;
        }

        foreach (var (name, type) in filtered)
            if (ModuleHelper.IsModuleLoaded(command.GuildId!.Value, type))
                message.AppendLine($"+ {name}");
            else
                message.AppendLine($"- {name}");
        message.AppendLine("```");
        await command.RespondAsync(message.ToString());
    }

    [ModuleCommand(GuildPermission.Administrator)]
    [CommandGuildOnly]
    public async Task UnloadModule(SocketSlashCommand command, string moduleID)
    {
        await command.DeferAsync();
        if (!ModuleHelper.IsModuleLoaded(command.GuildId!.Value, ModuleHelper.TypeMap[moduleID]))
        {
            await command.SendOrModifyOriginalResponse($"Module `{moduleID}` is not loaded.");
            return;
        }

        ModuleHelper.UnloadModule(command.GuildId!.Value, ModuleHelper.TypeMap[moduleID]);
        await CommandHelper.RefreshCommands(command.GuildId.Value);
        await command.SendOrModifyOriginalResponse($"Unloaded module `{moduleID}`.");
    }

    [ModuleCommand(GuildPermission.Administrator)]
    [CommandGuildOnly]
    public static async Task LoadModule(SocketSlashCommand command, string moduleID)
    {
        await command.DeferAsync();

        switch (ModuleHelper.TryLoadModule(command.GuildId!.Value, moduleID, out var exception, out var failReason))
        {
            case ModuleLoadResult.Loaded:
                await CommandHelper.RefreshCommands(command.GuildId.Value);
                await command.SendOrModifyOriginalResponse($"Module `{moduleID}` loaded.");
                break;

            case ModuleLoadResult.AlreadyLoaded:
                await command.SendOrModifyOriginalResponse($"Module `{moduleID}` is already loaded.");
                break;

            case ModuleLoadResult.LoadErrored:
                await command.SendOrModifyOriginalResponse(
                    $"Failed to load module `{moduleID}`:\n```\n{exception?.Message ?? "no exception?"}\n```");
                break;

            case ModuleLoadResult.LoadFailed:
                await command.SendOrModifyOriginalResponse($"Failed to load module `{moduleID}`: `{failReason}`");
                break;

            default:
            case ModuleLoadResult.NotFound:
                await command.SendOrModifyOriginalResponse(
                    $"Failed to load module `{moduleID}`: `unable to find module name in map`");
                break;
        }
    }
}
