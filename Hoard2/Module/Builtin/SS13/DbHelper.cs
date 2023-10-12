using System.Text;
using Discord;
using Discord.WebSocket;
using MySqlConnector;

namespace Hoard2.Module.Builtin.SS13
{
    public class DbHelper : ModuleBase
    {
        public override List<Type> GetConfigKnownTypes() => new()
        {
            typeof((string, string)),
        };

        public DbHelper(string configPath) : base(configPath)
        {
        }

        private async Task<MySqlDataReader?> Query(ulong guild, string query,
            Dictionary<string, object?>? arguments = null)
        {
            try
            {
                var (address, schema) = GetDatabaseAddressSchema(guild);
                var (user, pass) = GetDatabaseUserPass(guild);

                var dbClient =
                    new MySqlConnection($"Server={address};Database={schema};UID={user};PWD={pass}");
                await dbClient.OpenAsync();

                var command = dbClient.CreateCommand();
                command.CommandText = query;

                if (arguments is not null)
                    foreach (var (name, arg) in arguments)
                    {
                        var param = command.CreateParameter();
                        param.ParameterName = name;
                        param.Value = arg;
                        command.Parameters.Add(param);
                    }

                await command.PrepareAsync();
                var result =  await command.ExecuteReaderAsync();
                return result;
            }
            catch (Exception exception)
            {
                HoardMain.Logger.LogError("Exception during query execution: {Query} -> {Exception}", query,
                    exception.Message);
                return null;
            }
        }

        private (string, string) GetDatabaseAddressSchema(ulong guild) =>
            GuildConfig(guild).Get("database-address", ("localhost", "ss13"));

        private void SetDatabaseAddressSchema(ulong guild, (string, string) addressSchema) =>
            GuildConfig(guild).Set("database-address", addressSchema);

        private (string, string) GetDatabaseUserPass(ulong guild) =>
            GuildConfig(guild).Get("database-user-pass", ("defaultUsername", "defaultPassword"));

        private void SetDatabaseUserPass(ulong guild, (string, string) userPass) =>
            GuildConfig(guild).Set("database-user-pass", userPass);

        public async Task<string?> GetLinkedByondAccountFor(IGuildUser target)
        {
            var result = await Query(target.GuildId, "SELECT ckey FROM discord_links WHERE discord_id = @id",
                new Dictionary<string, object?>
                {
                    { "id", target.Id }
                });
            await (result?.ReadAsync() ?? Task.CompletedTask);
            return result?.GetString("ckey");
        }

        public async Task<ulong?> GetLinkedDiscordAccountFor(ulong guild, string ckey)
        {
            var result = await Query(guild, "SELECT discord_id FROM discord_links WHERE ckey = @ckey",
                new Dictionary<string, object?>
                {
                    { "ckey", ckey }
                });
            await (result?.ReadAsync() ?? Task.CompletedTask);
            return result?.GetUInt64("discord_id");
        }

        [ModuleCommand(GuildPermission.Administrator)]
        [CommandGuildOnly]
        public async Task SetDatabaseAddressSchema(SocketSlashCommand command, string address)
        {
            var addressSchemaSplit = address.Split(';');
            if (addressSchemaSplit.Length != 2)
            {
                await command.RespondAsync("Invalid format. expected `address;schema`", ephemeral: true);
                return;
            }

            SetDatabaseAddressSchema(command.GuildId!.Value, (addressSchemaSplit[0], addressSchemaSplit[1]));
            await command.RespondAsync("Updated the address", ephemeral: true);
        }

        [ModuleCommand(GuildPermission.Administrator)]
        [CommandGuildOnly]
        public async Task SetDatabaseUserPass(SocketSlashCommand command, string userPass)
        {
            var userPassSplit = userPass.Split(';');
            if (userPassSplit.Length != 2)
            {
                await command.RespondAsync("Invalid format. expected `user;pass`", ephemeral: true);
                return;
            }

            SetDatabaseUserPass(command.GuildId!.Value, (userPassSplit[0], userPassSplit[1]));
            await command.RespondAsync("Updated.", ephemeral: true);
        }

        [ModuleCommand(GuildPermission.ManageRoles)]
        [CommandGuildOnly]
        public async Task WhoIs(SocketSlashCommand command, IGuildUser? targetUser = null, string? targetCkey = null)
        {
            // ReSharper disable once ArrangeRedundantParentheses
            if ((targetUser == null) == (targetCkey == null))
            {
                await command.RespondAsync("You must specify a target user or a target ckey but not both.",
                    ephemeral: true);
                return;
            }

            if (targetCkey is not null)
            {
                if (await GetLinkedDiscordAccountFor(command.GuildId!.Value, targetCkey) is not { } discordId)
                {
                    await command.RespondAsync(
                        "There is no discord account linked to that ckey or the database setup is invalid.");
                    return;
                }

                await command.RespondAsync($"`{targetCkey}` is linked to discord account: <@{discordId}>",
                    allowedMentions: AllowedMentions.None);
                return;
            }

            if (await GetLinkedByondAccountFor(targetUser!) is not { } ckey)
            {
                await command.RespondAsync("Their account is not linked or the database setup is invalid.");
                return;
            }

            await command.RespondAsync($"{targetUser!.Mention} is linked to ckey: `{ckey}`",
                allowedMentions: AllowedMentions.None);
        }

        [ModuleCommand(GuildPermission.Administrator)]
        [CommandGuildOnly]
        public async Task GetPlayerRounds(SocketSlashCommand command, string? ckey = null, bool? getAdminsOnly = false,
            long months = 1)
        {
            if (getAdminsOnly is true && ckey is not null)
            {
                await command.RespondAsync("Cannot specify both admins only and a ckey!", ephemeral: true);
                return;
            }

            var (address, schema) = GetDatabaseAddressSchema(command.GuildId!.Value);
            var (user, pass) = GetDatabaseUserPass(command.GuildId.Value);

            var dbClient = new MySqlConnection($"Server={address};Database={schema};UID={user};PWD={pass}");
            await dbClient.OpenAsync();

            var dbCommand = dbClient.CreateCommand();
            var ckeyParam = ckey is not null ? dbCommand.CreateParameter() : null;
            if (ckeyParam is not null)
            {
                ckeyParam.Value = ckey;
                ckeyParam.ParameterName = "ckey";
                dbCommand.Parameters.Add(ckeyParam);
            }

            var monthParam = dbCommand.CreateParameter();
            monthParam.Value = months;
            monthParam.ParameterName = "months";
            dbCommand.Parameters.Add(monthParam);

            var ckeyInsert = ckey is not null ? "ckey = @ckey AND" : "";
            var adminInsert = getAdminsOnly is true ? "INNER JOIN admin ON connection_log.ckey = admin.ckey\n" : "";
            dbCommand.CommandText =
                "WITH DISTINCT_ROUNDS AS (SELECT DISTINCT connection_log.ckey, round_id\n" +
                "FROM connection_log\n" +
                adminInsert +
                "WHERE " +
                ckeyInsert +
                " datetime >= DATE_SUB(NOW(), INTERVAL @months MONTH))\n" +
                "SELECT DISTINCT_ROUNDS.ckey, COUNT(round_id)\n" +
                "FROM DISTINCT_ROUNDS\n" +
                "GROUP BY DISTINCT_ROUNDS.ckey\n" +
                "ORDER BY COUNT(round_id) DESC\n" +
                "LIMIT 20\n";

            await dbCommand.PrepareAsync();
            var reader = await dbCommand.ExecuteReaderAsync();
            var response = new StringBuilder("Query Results:\n```\n");
            while (await reader.ReadAsync())
                response.AppendLine($"- {reader[0]} | {reader[1]} rounds");
            response.AppendLine("```");
            await command.RespondAsync(response.ToString());
        }
    }
}
