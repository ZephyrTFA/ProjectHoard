using System.Runtime.CompilerServices;
using System.Text;
using Discord;
using Discord.WebSocket;
using Hoard2.Util;
using Octokit;
using Octokit.Internal;
using Tgstation.Server.Api.Models;
using Tgstation.Server.Api.Models.Request;
using Tgstation.Server.Api.Models.Response;
using Tgstation.Server.Client;
using Tgstation.Server.Client.Components;
using ProductHeaderValue = System.Net.Http.Headers.ProductHeaderValue;

namespace Hoard2.Module.Builtin.SS13;

public class TgsServerInformation
{
    public string ServerAddress { get; set; } = string.Empty;

    public Uri ServerUri => new(ServerAddress);

    public long DefaultInstance { get; set; }

    public string TestMergeLabelName { get; set; } = "Test Merge Candidate";
}

public class TGSLink : ModuleBase
{
    public override List<Type> GetConfigKnownTypes()
    {
        return new List<Type>()
        {
            typeof(TgsServerInformation),
            typeof(Dictionary<ulong, (string, string)>),
            typeof((string, string))
        };
    }

    public TGSLink(string configPath) : base(configPath)
    {
    }

    public TgsServerInformation GetServerInformation(ulong guild)
    {
        return GuildConfig(guild).Get("server-info", new TgsServerInformation())!;
    }

    public void SetServerInformation(ulong guild, TgsServerInformation info)
    {
        GuildConfig(guild).Set("server-info", info);
    }

    private Dictionary<ulong, TokenResponse> _userTokenMap = new();
    private Dictionary<ulong, IServerClient> _userClientMap = new();
    private ServerClientFactory _userTgsClientFactory = new(new ProductHeaderValue("ProjectHoard-TgsLink"));

    private async Task<IServerClient?> GetUserTgsClient(Uri server, IGuildUser user)
    {
        if (_userClientMap.TryGetValue(user.Id, out var existingClient))
            if (existingClient.Token.ExpiresAt.CompareTo(DateTimeOffset.Now) > 0)
                return existingClient;

        var storedLoginMap =
            GuildConfig(user.GuildId).Get("user-login-store", new Dictionary<ulong, (string, string)>());
        if (storedLoginMap!.TryGetValue(user.Id, out var info))
            if (await DoUserLogin(server, user, info.Item1, info.Item2))
                return _userClientMap[user.Id];

        return null;
    }

    private async Task<bool> DoUserLogin(Uri server, IUser user, string username, string password)
    {
        var userClient = await _userTgsClientFactory.CreateFromLogin(server, username, password);
        if (userClient.Token.Bearer is null) return false;
        _userClientMap[user.Id] = userClient;
        return true;
    }

    [ModuleCommand(GuildPermission.Administrator)]
    [CommandGuildOnly]
    public async Task SetServerAddress(SocketSlashCommand command, string address)
    {
        var serverInfo = GetServerInformation(command.GuildId!.Value);
        serverInfo.ServerAddress = address;
        SetServerInformation(command.GuildId.Value, serverInfo);
        await command.RespondAsync("Updated the server address.");
    }

    [ModuleCommand(GuildPermission.Administrator)]
    [CommandGuildOnly]
    public async Task SetDefaultInstance(SocketSlashCommand command, long defaultId)
    {
        var info = GetServerInformation(command.GuildId!.Value);
        info.DefaultInstance = defaultId;
        SetServerInformation(command.GuildId.Value, info);
        await command.RespondAsync("Updated the default instance id.");
    }

    public void UpdateStoredLoginInformation(IGuildUser user, string username, string password)
    {
        var guildConfig = GuildConfig(user.GuildId);
        var map = guildConfig.Get("user-login-store", new Dictionary<ulong, (string, string)>());
        map![user.Id] = (username, password);
        guildConfig.Set("user-login-store", map);
    }

    public (string, string)? GetStoredLoginInformation(IGuildUser user)
    {
        var guildConfig = GuildConfig(user.GuildId).Get("user-login-store", new Dictionary<ulong, (string, string)>());
        if (guildConfig!.TryGetValue(user.Id, out var value))
            return value;
        return null;
    }

    [ModuleCommand]
    [CommandGuildOnly]
    public async Task Login(SocketSlashCommand command, string username, string password,
        bool storeLoginInformation = false)
    {
        await command.DeferAsync(true);
        var serverInfo = GetServerInformation(command.GuildId!.Value);
        if (await DoUserLogin(serverInfo.ServerUri, command.User, username, password))
        {
            if (storeLoginInformation)
                UpdateStoredLoginInformation((IGuildUser)command.User, username, password);
            await command.SendOrModifyOriginalResponse("Logged in.");
        }
        else
        {
            await command.SendOrModifyOriginalResponse("Failed to login.");
        }
    }

    public static async Task<IInstanceClient> GetInstanceById(IServerClient client, long instanceId)
    {
        return client.Instances.CreateClient(
            await client.Instances.GetId(new EntityId { Id = instanceId }, default));
    }

    [ModuleCommand]
    [CommandGuildOnly]
    public async Task GetActiveTestMerges(SocketSlashCommand command, long instanceId = -1)
    {
        var serverInfo = GetServerInformation(command.GuildId!.Value);
        if (await GetUserTgsClient(serverInfo.ServerUri, (IGuildUser)command.User) is not { } client)
        {
            await command.RespondAsync("You must login first.");
            return;
        }

        await command.DeferAsync();
        if (instanceId is -1)
            instanceId = serverInfo.DefaultInstance;
        var instance = await GetInstanceById(client, instanceId);

        var repository = await instance.Repository.Read(default);
        if (repository.RevisionInformation is null)
        {
            await command.SendOrModifyOriginalResponse("No revision information found.");
            return;
        }

        var testMerges = repository.RevisionInformation.ActiveTestMerges?.ToList() ?? new List<TestMerge>();
        if (!testMerges.Any())
        {
            await command.SendOrModifyOriginalResponse("No test merges.");
            return;
        }

        var longestPrNum = testMerges.Max(tm => tm.Number).ToString().Length;
        var responseBuilder = new StringBuilder("Active Test Merges:\n```\n");
        foreach (var testMergeInfo in testMerges)
        {
            var title = testMergeInfo.TitleAtMerge ?? "NO TITLE";
            if (title.Length > 64)
                title = title[..64];
            responseBuilder.AppendLine($"#{testMergeInfo.Number.ToString($"D{longestPrNum}")} | {title}");
            responseBuilder.AppendLine($"\t- @{testMergeInfo.TargetCommitSha ?? "HEAD"}");
        }

        responseBuilder.AppendLine("```");

        await command.SendOrModifyOriginalResponse(responseBuilder.ToString());
    }

    [ModuleCommand]
    [CommandGuildOnly]
    public async Task DreamDaemonPanel(SocketSlashCommand command, long instance = -1)
    {
        var serverInfo = GetServerInformation(command.GuildId!.Value);
        if (instance is -1) instance = serverInfo.DefaultInstance;

        if (await GetUserTgsClient(serverInfo.ServerUri, (IGuildUser)command.User) is not { } client)
        {
            await command.RespondAsync("Login first.");
            return;
        }

        await command.RespondAsync("caching");
        _ = DoDreamDaemonPanel(await command.GetOriginalResponseAsync(), (IGuildUser)command.User,
            await GetInstanceById(client, instance));
    }

    [ModuleCommand]
    [CommandGuildOnly]
    public async Task TestMergeMenu(SocketSlashCommand command, bool markedOnly = true, long instance = -1)
    {
        var serverInfo = GetServerInformation(command.GuildId!.Value);
        if (instance is -1) instance = serverInfo.DefaultInstance;

        if (await GetUserTgsClient(serverInfo.ServerUri, (IGuildUser)command.User) is not { } client)
        {
            await command.RespondAsync("Login first.");
            return;
        }

        _ = DoTestMergePanel((command, null), (IGuildUser)command.User, await GetInstanceById(client, instance),
            onlyMarked: markedOnly);
    }

    [ModuleCommand]
    [CommandGuildOnly]
    public async Task UpdateRepo(SocketSlashCommand command, bool updateTms = false, bool compileWhenDone = false,
        long instance = -1)
    {
        var serverInfo = GetServerInformation(command.GuildId!.Value);
        if (instance is -1) instance = serverInfo.DefaultInstance;

        if (await GetUserTgsClient(serverInfo.ServerUri, (IGuildUser)command.User) is not { } client)
        {
            await command.RespondAsync("Login first.");
            return;
        }

        await command.DeferAsync();

        var instanceClient = await GetInstanceById(client, instance);
        var currentState = await instanceClient.Repository.Read(default);
        var newState = new RepositoryUpdateRequest
        {
            UpdateFromOrigin = true,
            Reference = currentState.Reference
        };

        var testMerges = new List<TestMergeParameters>();
        var githubClient = new GitHubClient(new Octokit.ProductHeaderValue("TgsLink"));
        if (currentState.RevisionInformation is { } currentRevision)
            testMerges.AddRange((currentRevision.ActiveTestMerges ?? ArraySegment<TestMerge>.Empty).Select(existingTm =>
            {
                var tmParams = new TestMergeParameters
                {
                    Comment = existingTm.Comment, Number = existingTm.Number,
                };
                if (!updateTms)
                    tmParams.TargetCommitSha = existingTm.TargetCommitSha;
                return tmParams;
            }).Where(tm =>
            {
                var pullStatus = githubClient.Repository.PullRequest.Get(
                    currentState.RemoteRepositoryOwner,
                    currentState.RemoteRepositoryName, tm.Number).GetAwaiter().GetResult();
                return pullStatus is { Merged: false };
            }));
        newState.NewTestMerges = testMerges;

        await command.SendOrModifyOriginalResponse("Updating...");
        var response = await instanceClient.Repository.Update(newState, default);
        if (response.ActiveJob is { } job)
        {
            do
            {
                job = await instanceClient.Jobs.GetId(new EntityId { Id = job.Id!.Value }, default);
            } while (job.StoppedAt is null);

            if (job.ExceptionDetails is not null)
            {
                await command.SendOrModifyOriginalResponse($"Repo Update Failed: {job.ExceptionDetails}");
                return;
            }
        }

        await command.SendOrModifyOriginalResponse($"Updated.{(compileWhenDone ? " Starting Compilation." : "")}");
        if (compileWhenDone)
            StartCompileJob(command.Channel, instanceClient);
    }

    [ModuleCommand(GuildPermission.Administrator)]
    [CommandGuildOnly]
    public async Task SetTestMergeCandidateLabel(SocketSlashCommand command, string labelName)
    {
        var serverInfo = GetServerInformation(command.GuildId!.Value);
        serverInfo.TestMergeLabelName = labelName;
        await command.RespondAsync("Updated the label name.");
    }

    private List<TaskAwaiter> _compileJobs = new();

    [ModuleCommand]
    [CommandGuildOnly]
    public async Task TriggerCompile(SocketSlashCommand command, long instance = -1)
    {
        var serverInfo = GetServerInformation(command.GuildId!.Value);
        if (instance is -1) instance = serverInfo.DefaultInstance;

        if (await GetUserTgsClient(serverInfo.ServerUri, (IGuildUser)command.User) is not { } client)
        {
            await command.RespondAsync("Login first.", ephemeral: true);
            return;
        }

        StartCompileJob(command.Channel, await GetInstanceById(client, instance));
        await command.RespondAsync("Compilation Triggered", ephemeral: true);
    }

    private void StartCompileJob(IMessageChannel channel, IInstanceClient client)
    {
        var compileTask = StartAndWaitCompile(channel, client).GetAwaiter();
        _compileJobs.Add(compileTask);
        compileTask.OnCompleted(() => _compileJobs.Remove(compileTask));
    }

    // (user, instance) -> (channel, message)
    private Dictionary<(ulong, long), (ulong, ulong)> _daemonPanelStore = new();
    private Dictionary<(ulong, long), Timer> _daemonPanelTimeoutStore = new();

    private async Task<IUserMessage?> GetDaemonPanelMessage(IUser user, long instance)
    {
        if (!_daemonPanelStore.TryGetValue((user.Id, instance), out var match))
            return null;

        var channel = await HoardMain.DiscordClient.GetChannelAsync(match.Item1);
        if (channel is not IMessageChannel messageChannel)
            return null;

        return await messageChannel.GetMessageAsync(match.Item2) as IUserMessage;
    }

    public async Task DoDreamDaemonPanel(
        IUserMessage holder,
        IGuildUser user,
        IInstanceClient instanceClient,
        bool block = false,
        bool timeout = false)
    {
        var currentState = await instanceClient.DreamDaemon.Read(default);
        var blockButtons = block || timeout;
        var existing = await GetDaemonPanelMessage(user, (long)instanceClient.Metadata.Id!);
        if (existing is not null && existing.Id != holder.Id)
            await existing.DeleteAsync();

        var key = (user.Id, (long)instanceClient.Metadata.Id);
        _daemonPanelStore[key] = (holder.Channel.Id, holder.Id);

        if (_daemonPanelTimeoutStore.TryGetValue(key, out var timer))
            await timer.DisposeAsync();
        _daemonPanelTimeoutStore[key] =
            new Timer(state => { _ = DoDreamDaemonPanel(holder, user, instanceClient, timeout: true); }, null,
                TimeSpan.FromSeconds(15), Timeout.InfiniteTimeSpan);

        var embedData = new EmbedBuilder()
            .WithTitle($"Dream Daemon - {instanceClient.Metadata.Name!}")
            .WithColor(currentState.Status! switch
            {
                WatchdogStatus.Offline => Color.Red,
                WatchdogStatus.Restoring => Color.Purple,
                WatchdogStatus.Online => Color.Green,
                WatchdogStatus.DelayedRestart => Color.Orange,
                _ => Color.DarkGrey
            })
            .AddField("Watchdog State", currentState.Status!.ToString());

        var shutdownButton = CreateButton($"dd-shutdown-{instanceClient.Metadata.Id}", user.Id)
            .WithLabel("Shutdown")
            .WithStyle(ButtonStyle.Danger)
            .WithDisabled(blockButtons || currentState.Status is WatchdogStatus.Offline)
            .Build();

        var launchButton = CreateButton($"dd-launch-{instanceClient.Metadata.Id}", user.Id)
            .WithLabel("Launch")
            .WithStyle(ButtonStyle.Success)
            .WithDisabled(blockButtons || currentState.Status is not WatchdogStatus.Offline)
            .Build();

        var compileButton = CreateButton($"dm-compile-{instanceClient.Metadata.Id}", user.Id)
            .WithLabel("Compile")
            .WithStyle(ButtonStyle.Primary)
            .WithDisabled(blockButtons)
            .Build();

        var componentBuilder = new ComponentBuilder()
            .AddRow(new ActionRowBuilder().WithComponents(new List<IMessageComponent> { launchButton, shutdownButton }))
            .AddRow(new ActionRowBuilder().WithComponents(new List<IMessageComponent> { compileButton }));

        await holder.ModifyAsync(props =>
        {
            props.Content = timeout ? "Disabled due to inactivity." : block ? "Working..." : "";
            props.Components = new Optional<MessageComponent>(componentBuilder.Build());
            props.Embeds = new Optional<Embed[]>(new[] { embedData.Build() });
        });
    }

    private Dictionary<(ulong, long), Timer> _testMergePanelTimeoutStore = new();
    private Dictionary<(ulong, long), SelectMenuBuilder> _testMergePanelMenuStore = new();

    public async Task DoTestMergePanel(
        (SocketSlashCommand?, IUserMessage?) holder,
        IGuildUser user,
        IInstanceClient instanceClient,
        bool finished = false,
        bool onlyMarked = true
    )
    {
        await (holder.Item1?.DeferAsync() ?? Task.CompletedTask);

        var currentState = await instanceClient.Repository.Read(default);
        var userGhToken =
            GuildConfig(user.GuildId).Get("user-gh-token-map", new Dictionary<ulong, string?>())!.GetValueOrDefault(
                user.Id, null);
        var ghClient = new GitHubClient(new Octokit.ProductHeaderValue("TgsLink"),
            new InMemoryCredentialStore(userGhToken is null ? Credentials.Anonymous : new Credentials(userGhToken)));

        var key = (user.Id, instanceClient.Metadata.Id!.Value);
        if (_testMergePanelTimeoutStore.TryGetValue(key, out var timer))
            await timer.DisposeAsync();

        if (finished)
        {
            await holder.Item2!.ModifyAsync(props =>
            {
                var replacementMenu = _testMergePanelMenuStore[key].WithDisabled(true);
                props.Content = "Test Merge Panel Outdated.";
                props.Components = new Optional<MessageComponent>(new ComponentBuilder()
                    .WithSelectMenu(replacementMenu)
                    .Build());
            });
            return;
        }

        var repositoryTMs = currentState.RevisionInformation?.ActiveTestMerges ?? new List<TestMerge>();
        var githubPRs = await ghClient.PullRequest.GetAllForRepository(currentState.RemoteRepositoryOwner,
            currentState.RemoteRepositoryName);
        var testMergeMenu = CreateMenu($"repo-tmpanel-{instanceClient.Metadata.Id}-{user.Username}")
            .WithPlaceholder("Select Pulls to Test Merge")
            .WithType(ComponentType.SelectMenu)
            .WithMinValues(0);
        _testMergePanelMenuStore[key] = testMergeMenu;

        var existingTms = githubPRs.Where(ghPr => repositoryTMs.Any(rTm => rTm.Number == ghPr.Number)).ToList();
        var availablePrs = githubPRs.Where(ghPr => !existingTms.Contains(ghPr)).ToList();

        var testMergeLabelName = GetServerInformation(user.GuildId).TestMergeLabelName;
        if (onlyMarked)
            availablePrs = availablePrs.Where(pr => pr.Labels.Any(label => label.Name == testMergeLabelName)).ToList();

        var totalOptions = existingTms.Count + availablePrs.Count;
        var skipped = totalOptions > 25 ? totalOptions - 25 : 0;
        if (skipped > 0)
            availablePrs = availablePrs.SkipLast(skipped).ToList();
        testMergeMenu.WithMaxValues(totalOptions - skipped);

        string Truncate(string @string, int length)
        {
            if (@string.Length > length)
                return @string[..(length - 3)] + "...";
            return @string;
        }

        foreach (var existingTm in existingTms.OrderBy(tm => tm.Number))
        {
            var title = Truncate($"#{existingTm.Number} - {existingTm.Title}", 100);
            testMergeMenu.AddOption(title, existingTm.Number.ToString(), isDefault: true);
        }

        foreach (var availableTm in availablePrs)
        {
            var title = Truncate($"#{availableTm.Number} - {availableTm.Title}", 100);
            testMergeMenu.AddOption(title, availableTm.Number.ToString());
        }

        var components = new ComponentBuilder()
            .WithSelectMenu(testMergeMenu)
            .Build();
        await holder.Item1!.ModifyOriginalResponseAsync(props =>
            {
                props.Content =
                    $"Test Merge Menu - {instanceClient.Metadata.Name}{(skipped > 0 ? $"\nSkipped {skipped} PRs due to discord api limits." : "")}";
                props.Components = new Optional<MessageComponent>(components);
            }
        );

        _testMergePanelTimeoutStore[(user.Id, instanceClient.Metadata.Id!.Value)] = new Timer(_ =>
        {
            holder.Item1.GetOriginalResponseAsync().GetAwaiter().GetResult().ModifyAsync(props =>
            {
                props.Content = "Test Merge Menu Outdated.";
                props.Components = new Optional<MessageComponent>(new ComponentBuilder()
                    .WithSelectMenu(_testMergePanelMenuStore[key].WithDisabled(true)).Build());
            });
        }, null, TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
    }

    public static async Task StartAndWaitCompile(IMessageChannel channel, IInstanceClient instanceClient)
    {
        var message = await channel.SendMessageAsync("Caching Compile Context...");
        var job = await instanceClient!.DreamMaker.Compile(default);
        var lastProgress = 0;
        do
        {
            job = await instanceClient.Jobs.GetId(job, default);
            if (job.Progress.HasValue && job.Progress.Value != lastProgress)
            {
                lastProgress = job.Progress.Value;
                await message.ModifyAsync(props =>
                    props.Content =
                        $"Compiling:\n`{job.Stage}`\n`[{GetProgressBar(job.Progress!.Value, 10)}] {job.Progress.Value}%`");
            }

            await Task.Delay(125);
        } while (job.StoppedAt is null);

        if (job.ExceptionDetails is not null)
            await message.ModifyAsync(props =>
                props.Content = $"Failed to Compile: `{job.ExceptionDetails}`");
        else
            await message.ModifyAsync(props => props.Content = "Compilation Successful.");
        return;

        string GetProgressBar(int percentage, ushort barLength)
        {
            switch (percentage)
            {
                case >= 100: return new string('=', barLength);
                case <= 0: return new string(' ', barLength);

                default:
                    var barsFilled = (ushort)Math.Floor(percentage * barLength / 100f);
                    return new string('=', barsFilled) + new string(' ', barLength - barsFilled);
            }
        }
    }

    public override async Task OnButton(string buttonId, SocketMessageComponent button)
    {
        var data = buttonId.Split('-');
        var serverClient = await GetUserTgsClient(GetServerInformation(button.GuildId!.Value).ServerUri,
            (IGuildUser)button.User);
        long? instance = data.Length >= 2 ? long.Parse(data[2]) : null;
        var instanceClient = serverClient is not null && instance is not null
            ? await GetInstanceById(serverClient, instance.Value)
            : null;
        switch (data[0])
        {
            case "dm":
                switch (data[1])
                {
                    case "compile":
                        var channel = (IMessageChannel)await button.GetChannelAsync();
                        await StartAndWaitCompile(channel, instanceClient!);
                        return;

                    default:
                        throw new NotImplementedException($"Unknown dd action: {data[1]}");
                }

            case "dd":
                if (instance is null || instanceClient is null)
                    return;
                var panelMessage = (await GetDaemonPanelMessage(button.User, instance.Value))!;
                switch (data[1])
                {
                    case "shutdown":
                        await button.RespondAsync("Shutting down...");
                        await instanceClient.DreamDaemon.Shutdown(default);
                        break;

                    case "launch":
                        await button.RespondAsync("Launching...");
                        // block the panel
                        await DoDreamDaemonPanel(panelMessage, (IGuildUser)button.User, instanceClient, true);
                        var jobResponse = await instanceClient.DreamDaemon.Start(default);
                        do
                        {
                            jobResponse = await instanceClient.Jobs.GetId(jobResponse, default);
                        } while (jobResponse.StoppedAt is null);

                        break;

                    default:
                        throw new NotImplementedException($"Unknown dd command: {data[1]}");
                }

                // kick off a refresh, and don't wait
                _ = DoDreamDaemonPanel(panelMessage, (IGuildUser)button.User, instanceClient);
                break;

            default:
                throw new NotImplementedException($"Unknown button handler: {data[0]}");
        }
    }

    public override async Task OnMenu(string menuId, SocketMessageComponent menu)
    {
        var data = menuId.Split('-');
        switch (data[0])
        {
            case "repo":
                var action = data[1];
                var instanceId = long.Parse(data[2]);
                var serverInfo = GetServerInformation(menu.GuildId!.Value);
                var tgsClient = await GetUserTgsClient(serverInfo.ServerUri, (IGuildUser)menu.User);
                if (tgsClient is null)
                    return;
                var instanceClient = await GetInstanceById(tgsClient, instanceId);
                switch (action)
                {
                    case "tmpanel":
                        var username = data[3];
                        if (!username.Equals(menu.User.Username))
                        {
                            await menu.RespondAsync("Bad User.", ephemeral: true);
                            return;
                        }

                        _ = DoTestMergePanel((null, menu.Message), (IGuildUser)menu.User, instanceClient, true);
                        await menu.RespondAsync("Updating TMs...");

                        var expectedTms = menu.Data.Values.Select(int.Parse);
                        var repositoryRequest = new RepositoryUpdateRequest
                        {
                            UpdateFromOrigin = true,
                            Reference = (await instanceClient.Repository.Read(default)).Reference,
                            NewTestMerges = expectedTms.Select(tm => new TestMergeParameters { Number = tm }).ToList()
                        };
                        var updateResponse = await instanceClient.Repository.Update(repositoryRequest, default);
                        var job = updateResponse.ActiveJob;
                        if (job is not null)
                        {
                            do
                            {
                                job = await instanceClient.Jobs.GetId(job, default);
                            } while (job.StoppedAt is null);

                            if (job.ExceptionDetails is not null)
                            {
                                await menu.ModifyOriginalResponseAsync(props =>
                                    props.Content = $"Failed to update TMs: `{job.ExceptionDetails}`");
                                return;
                            }
                        }

                        await menu.ModifyOriginalResponseAsync(props => props.Content = "Updated TMs.");
                        return;

                    default:
                        throw new NotImplementedException($"Unknown repo action: {action}");
                }

            default:
                throw new NotImplementedException($"Unknown menu handler: {data[0]}");
        }
    }
}
