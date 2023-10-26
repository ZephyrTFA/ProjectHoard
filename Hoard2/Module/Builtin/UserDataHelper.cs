using System.ComponentModel;
using Discord;
using Discord.WebSocket;

namespace Hoard2.Module.Builtin;

public class UserDataHelper : ModuleBase
{
    public const string GlobalInformation = "GLOBAL-DATA";

    public const string NicknamesKey = "nicknames";
    public const string UsernamesKey = "usernames";
    public const string ProfilePicturesKey = "profilepics";

    public UserDataHelper(string configPath) : base(configPath)
    {
    }

    public ModuleConfig GetUserConfig(ulong user, string module)
    {
        return CustomConfig($"{user}-{module}-{0}");
    }

    public ModuleConfig GetGuildUserConfig(ulong user, ulong guild, string module)
    {
        return CustomConfig($"{user}-{module}-{guild}");
    }

    public override List<Type> GetConfigKnownTypes()
    {
        return new[]
        {
            typeof(List<string>)
        }.ToList();
    }

    public void AddGuildNicknameChange(IGuildUser user)
    {
        var userConfig = GetGuildUserConfig(user.Id, user.GuildId, GlobalInformation);
        var userNicks = userConfig.Get(NicknamesKey, new List<string>())!;
        userNicks.Add(user.Nickname ?? "!NONE!");
        userConfig.Set(NicknamesKey, userNicks);
    }

    public void AddUsernameChange(IUser user)
    {
        var userConfig = GetUserConfig(user.Id, GlobalInformation);
        var userUsernames = userConfig.Get(UsernamesKey, new List<string>())!;
        userUsernames.Add(user.Username);
        userConfig.Set(UsernamesKey, userUsernames);
    }

    public void AddProfilePictureChange(IUser user)
    {
        var userConfig = GetUserConfig(user.Id, GlobalInformation);
        var userProfilePictures = userConfig.Get(ProfilePicturesKey, new List<string>())!;
        userProfilePictures.Add(user.GetAvatarUrl());
        userConfig.Set(ProfilePicturesKey, userProfilePictures);
    }

    public List<string> GetGuildNicknameHistory(IGuildUser user)
    {
        var userConfig = GetGuildUserConfig(user.Id, user.GuildId, GlobalInformation);
        var userNicks = userConfig.Get(NicknamesKey, new List<string>())!;
        return userNicks;
    }

    public List<string> GetUsernameHistory(IUser user)
    {
        var userConfig = GetUserConfig(user.Id, GlobalInformation);
        var userUsernames = userConfig.Get(UsernamesKey, new List<string>())!;
        return userUsernames;
    }

    public List<string> GetProfilePictureHistory(IUser user)
    {
        var userConfig = GetUserConfig(user.Id, GlobalInformation);
        var userProfilePictureHistory = userConfig.Get(ProfilePicturesKey, new List<string>())!;
        return userProfilePictureHistory;
    }

    [ModuleCommand]
    [Description("Get all of your stored personal data.")]
    public async Task GetUserData(SocketSlashCommand command)
    {
        await command.RespondAsync("Not implemented");
    }
}