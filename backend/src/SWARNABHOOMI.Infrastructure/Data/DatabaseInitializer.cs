using System.Data;
using Microsoft.EntityFrameworkCore;

namespace SWARNABHOOMI.Infrastructure.Data;

public static class DatabaseInitializer
{
    public static void Migrate(SWARNABHOOMIContext context)
    {
        if (!context.Database.IsRelational())
        {
            context.Database.EnsureCreated();
            return;
        }

        if (!IsSqlite(context) && context.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            throw new InvalidOperationException(
                $"Database migrations are not configured for provider '{context.Database.ProviderName}'.");
        }

        var appliedMigrations = context.Database.GetAppliedMigrations().ToArray();
        if (appliedMigrations.Length == 0 && HasLegacySchema(context))
        {
            StampInitialSchema(context);
        }

        context.Database.Migrate();
    }

    private static bool HasLegacySchema(SWARNABHOOMIContext context)
    {
        var connection = context.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            connection.Open();
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = IsSqlite(context)
                ? "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Users'"
                : "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = 'Users'";
            return Convert.ToInt32(command.ExecuteScalar()) > 0;
        }
        finally
        {
            if (closeConnection)
            {
                connection.Close();
            }
        }
    }

    private static void StampInitialSchema(SWARNABHOOMIContext context)
    {
        var initialMigration = context.Database.GetMigrations().FirstOrDefault()
            ?? throw new InvalidOperationException("The initial database migration is missing.");

        var createHistoryTable = IsSqlite(context)
            ? """
              CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                  "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                  "ProductVersion" TEXT NOT NULL
              );
              """
            : """
              CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                  "MigrationId" character varying(150) NOT NULL,
                  "ProductVersion" character varying(32) NOT NULL,
                  CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
              );
              """;

        context.Database.ExecuteSqlRaw(createHistoryTable);
        context.Database.ExecuteSqlInterpolated(
            $"INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({initialMigration}, {typeof(DbContext).Assembly.GetName().Version?.ToString(3) ?? "10.0.0"})");
    }

    private static bool IsSqlite(SWARNABHOOMIContext context) =>
        context.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";
}
