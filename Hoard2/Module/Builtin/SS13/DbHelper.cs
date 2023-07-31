﻿using System.Text;

using Discord;
using Discord.WebSocket;

using MySqlConnector;

namespace Hoard2.Module.Builtin.SS13
{
	public class DbHelper : ModuleBase
	{
		public override List<Type> GetConfigKnownTypes() => new List<Type>
		{
			typeof((string, string)),
		};

		public DbHelper(string configPath) : base(configPath) { }

		(string, string) GetDatabaseAddressSchema(ulong guild) => GuildConfig(guild).Get("database-address", ("localhost", "ss13"));

		void SetDatabaseAddressSchema(ulong guild, (string, string) addressSchema) => GuildConfig(guild).Set("database-address", addressSchema);

		(string, string) GetDatabaseUserPass(ulong guild) => GuildConfig(guild).Get("database-user-pass", ("defaultUsername", "defaultPassword"));

		void SetDatabaseUserPass(ulong guild, (string, string) userPass) => GuildConfig(guild).Set("database-user-pass", userPass);

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

		[ModuleCommand(GuildPermission.Administrator)]
		[CommandGuildOnly]
		public async Task GetPlayerRounds(SocketSlashCommand command, string? ckey = null, long months = 1)
		{
			var (address, schema) = GetDatabaseAddressSchema(command.GuildId!.Value);
			var (user, pass) = GetDatabaseUserPass(command.GuildId.Value);

			var dbClient = new MySqlConnection($"Server={address};Database={schema};UID={user};PWD={pass}");
			dbClient.Open();

			var dbCommand = dbClient.CreateCommand();

			var ckeyParam = ckey is { } ? dbCommand.CreateParameter() : null;
			if (ckeyParam is { })
			{
				ckeyParam.Value = ckey;
				ckeyParam.ParameterName = "ckey";
				dbCommand.Parameters.Add(ckeyParam);
			}

			var monthParam = dbCommand.CreateParameter();
			monthParam.Value = months;
			monthParam.ParameterName = "months";
			dbCommand.Parameters.Add(monthParam);

			var commandText = 
				$"WITH DISTINCT_ROUNDS AS (SELECT DISTINCT ckey, round_id FROM connection_log " +
				$"WHERE {(ckey is { } ? "ckey LIKE @ckey AND" : "")} datetime >= DATE_ADD(NOW(), INTERVAL - @months MONTH)) " +
				$"SELECT DISTINCT_ROUNDS.ckey, COUNT(round_id) " +
				$"FROM DISTINCT_ROUNDS GROUP BY DISTINCT_ROUNDS.ckey ORDER BY COUNT(round_id) DESC LIMIT 20";
			dbCommand.CommandText = commandText;

			var reader = await dbCommand.ExecuteReaderAsync();
			var response = new StringBuilder("Query Results:\n```\n");
			while (await reader.NextResultAsync())
			{
				response.AppendLine($"- {reader[0]}");
				response.AppendLine($" - {reader[1]} rounds");
			}
			response.AppendLine("```");
			await command.RespondAsync(response.ToString());
		}
	}
}
