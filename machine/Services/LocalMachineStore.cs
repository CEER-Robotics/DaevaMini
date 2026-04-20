using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DaevaMini.Config;
using Microsoft.Data.Sqlite;

namespace DaevaMini.Services;

public sealed class LocalMachineStore
{
    private static LocalMachineStore? _instance;
    private static readonly object LockObject = new();

    private readonly string _connectionString;
    private bool _initialized;

    public static LocalMachineStore Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (LockObject)
                {
                    _instance ??= new LocalMachineStore();
                }
            }

            return _instance;
        }
    }

    private LocalMachineStore()
    {
        string dbPath = Path.Combine(AppContext.BaseDirectory, "daeva-machine.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    public void Initialize()
    {
        if (_initialized)
            return;

        lock (LockObject)
        {
            if (_initialized)
                return;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                PRAGMA journal_mode = WAL;

                CREATE TABLE IF NOT EXISTS config_documents (
                    profile_key TEXT PRIMARY KEY,
                    machine_variant TEXT NOT NULL,
                    config_json TEXT NOT NULL,
                    config_yaml TEXT NOT NULL,
                    version INTEGER NOT NULL,
                    source TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS machine_events (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    occurred_at_utc TEXT NOT NULL,
                    profile_key TEXT NOT NULL,
                    machine_variant TEXT NOT NULL,
                    category TEXT NOT NULL,
                    operation_type TEXT NOT NULL,
                    status TEXT NOT NULL,
                    cocktail_id TEXT NULL,
                    cocktail_name TEXT NULL,
                    mode_name TEXT NULL,
                    total_milliliters INTEGER NULL,
                    total_duration_ms INTEGER NULL,
                    payload_json TEXT NULL,
                    uploaded_at_utc TEXT NULL
                );

                CREATE TABLE IF NOT EXISTS machine_sync_state (
                    profile_key TEXT PRIMARY KEY,
                    machine_id TEXT NOT NULL,
                    applied_remote_config_version INTEGER NOT NULL,
                    last_heartbeat_at_utc TEXT NULL,
                    last_sync_error TEXT NULL,
                    updated_at_utc TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_machine_events_operation
                    ON machine_events(operation_type, status, occurred_at_utc);

                CREATE INDEX IF NOT EXISTS idx_machine_events_profile
                    ON machine_events(profile_key, occurred_at_utc);

                CREATE INDEX IF NOT EXISTS idx_machine_events_pending_upload
                    ON machine_events(profile_key, uploaded_at_utc, occurred_at_utc);
                """;
            command.ExecuteNonQuery();

            EnsureColumnExists(connection, "machine_events", "uploaded_at_utc", "TEXT NULL");
            _initialized = true;
        }
    }

    public void SeedConfigDocument(string profileKey, AppConfig config, string yaml, string source = "yaml-import")
    {
        Initialize();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT OR IGNORE INTO config_documents (
                profile_key,
                machine_variant,
                config_json,
                config_yaml,
                version,
                source,
                updated_at_utc
            )
            VALUES (
                $profile_key,
                $machine_variant,
                $config_json,
                $config_yaml,
                1,
                $source,
                $updated_at_utc
            );
            """;
        command.Parameters.AddWithValue("$profile_key", profileKey);
        command.Parameters.AddWithValue("$machine_variant", MachineProfileHelper.ResolveVariant(profileKey));
        command.Parameters.AddWithValue("$config_json", AppConfigSerialization.SerializeJson(config));
        command.Parameters.AddWithValue("$config_yaml", yaml);
        command.Parameters.AddWithValue("$source", source);
        command.Parameters.AddWithValue("$updated_at_utc", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public AppConfig? LoadConfig(string profileKey)
    {
        Initialize();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT config_json
            FROM config_documents
            WHERE profile_key = $profile_key
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$profile_key", profileKey);

        object? result = command.ExecuteScalar();
        return result is string json ? AppConfigSerialization.DeserializeJson(json) : null;
    }

    public int GetConfigVersion(string profileKey)
    {
        Initialize();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT version
            FROM config_documents
            WHERE profile_key = $profile_key
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$profile_key", profileKey);
        object? result = command.ExecuteScalar();
        return result is long value ? (int)value : 0;
    }

    public void SaveConfig(string profileKey, AppConfig config, string yaml, string source)
    {
        Initialize();

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO config_documents (
                profile_key,
                machine_variant,
                config_json,
                config_yaml,
                version,
                source,
                updated_at_utc
            )
            VALUES (
                $profile_key,
                $machine_variant,
                $config_json,
                $config_yaml,
                1,
                $source,
                $updated_at_utc
            )
            ON CONFLICT(profile_key) DO UPDATE SET
                machine_variant = excluded.machine_variant,
                config_json = excluded.config_json,
                config_yaml = excluded.config_yaml,
                version = config_documents.version + 1,
                source = excluded.source,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$profile_key", profileKey);
        command.Parameters.AddWithValue("$machine_variant", MachineProfileHelper.ResolveVariant(profileKey));
        command.Parameters.AddWithValue("$config_json", AppConfigSerialization.SerializeJson(config));
        command.Parameters.AddWithValue("$config_yaml", yaml);
        command.Parameters.AddWithValue("$source", source);
        command.Parameters.AddWithValue("$updated_at_utc", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();

        transaction.Commit();
    }

    public void SeedSyncState(string profileKey, string machineId, int appliedRemoteConfigVersion = 1)
    {
        Initialize();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT OR IGNORE INTO machine_sync_state (
                profile_key,
                machine_id,
                applied_remote_config_version,
                last_heartbeat_at_utc,
                last_sync_error,
                updated_at_utc
            )
            VALUES (
                $profile_key,
                $machine_id,
                $applied_remote_config_version,
                NULL,
                NULL,
                $updated_at_utc
            );
            """;
        command.Parameters.AddWithValue("$profile_key", profileKey);
        command.Parameters.AddWithValue("$machine_id", machineId);
        command.Parameters.AddWithValue("$applied_remote_config_version", appliedRemoteConfigVersion);
        command.Parameters.AddWithValue("$updated_at_utc", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public MachineSyncStateRecord GetSyncState(string profileKey)
    {
        Initialize();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT profile_key, machine_id, applied_remote_config_version, last_heartbeat_at_utc, last_sync_error
            FROM machine_sync_state
            WHERE profile_key = $profile_key
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$profile_key", profileKey);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return new MachineSyncStateRecord
            {
                ProfileKey = profileKey,
                MachineId = MachineProfileHelper.ResolveMachineId(profileKey),
                AppliedRemoteConfigVersion = 1
            };
        }

        return new MachineSyncStateRecord
        {
            ProfileKey = reader.GetString(0),
            MachineId = reader.GetString(1),
            AppliedRemoteConfigVersion = reader.GetInt32(2),
            LastHeartbeatAtUtc = reader.IsDBNull(3) ? null : DateTime.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
            LastSyncError = reader.IsDBNull(4) ? null : reader.GetString(4)
        };
    }

    public void UpdateAppliedRemoteConfigVersion(string profileKey, int appliedRemoteConfigVersion)
    {
        UpdateSyncState(profileKey, appliedRemoteConfigVersion, null, null);
    }

    public void UpdateHeartbeat(string profileKey)
    {
        UpdateSyncState(profileKey, null, DateTime.UtcNow, null);
    }

    public void RecordSyncError(string profileKey, string error)
    {
        UpdateSyncState(profileKey, null, null, error);
    }

    private void UpdateSyncState(string profileKey, int? appliedRemoteConfigVersion, DateTime? heartbeatAtUtc, string? error)
    {
        Initialize();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE machine_sync_state
            SET applied_remote_config_version = COALESCE($applied_remote_config_version, applied_remote_config_version),
                last_heartbeat_at_utc = COALESCE($last_heartbeat_at_utc, last_heartbeat_at_utc),
                last_sync_error = $last_sync_error,
                updated_at_utc = $updated_at_utc
            WHERE profile_key = $profile_key;
            """;
        command.Parameters.AddWithValue("$profile_key", profileKey);
        command.Parameters.AddWithValue("$applied_remote_config_version", (object?)appliedRemoteConfigVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$last_heartbeat_at_utc", heartbeatAtUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$last_sync_error", error ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$updated_at_utc", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public void RecordEvent(MachineEventRecord record)
    {
        Initialize();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO machine_events (
                occurred_at_utc,
                profile_key,
                machine_variant,
                category,
                operation_type,
                status,
                cocktail_id,
                cocktail_name,
                mode_name,
                total_milliliters,
                total_duration_ms,
                payload_json,
                uploaded_at_utc
            )
            VALUES (
                $occurred_at_utc,
                $profile_key,
                $machine_variant,
                $category,
                $operation_type,
                $status,
                $cocktail_id,
                $cocktail_name,
                $mode_name,
                $total_milliliters,
                $total_duration_ms,
                $payload_json,
                NULL
            );
            """;
        command.Parameters.AddWithValue("$occurred_at_utc", record.OccurredAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$profile_key", record.ProfileKey);
        command.Parameters.AddWithValue("$machine_variant", record.MachineVariant);
        command.Parameters.AddWithValue("$category", record.Category);
        command.Parameters.AddWithValue("$operation_type", record.OperationType);
        command.Parameters.AddWithValue("$status", record.Status);
        command.Parameters.AddWithValue("$cocktail_id", (object?)record.CocktailId ?? DBNull.Value);
        command.Parameters.AddWithValue("$cocktail_name", (object?)record.CocktailName ?? DBNull.Value);
        command.Parameters.AddWithValue("$mode_name", (object?)record.ModeName ?? DBNull.Value);
        command.Parameters.AddWithValue("$total_milliliters", (object?)record.TotalMilliliters ?? DBNull.Value);
        command.Parameters.AddWithValue("$total_duration_ms", (object?)record.TotalDurationMs ?? DBNull.Value);
        command.Parameters.AddWithValue("$payload_json", (object?)record.PayloadJson ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<PendingMachineEventRecord> GetPendingEvents(string profileKey, int limit = 100)
    {
        Initialize();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                occurred_at_utc,
                profile_key,
                machine_variant,
                category,
                operation_type,
                status,
                cocktail_id,
                cocktail_name,
                mode_name,
                total_milliliters,
                total_duration_ms,
                payload_json
            FROM machine_events
            WHERE profile_key = $profile_key
              AND uploaded_at_utc IS NULL
            ORDER BY occurred_at_utc, id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$profile_key", profileKey);
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var events = new List<PendingMachineEventRecord>();
        while (reader.Read())
        {
            events.Add(new PendingMachineEventRecord
            {
                Id = reader.GetInt64(0),
                OccurredAtUtc = DateTime.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind),
                ProfileKey = reader.GetString(2),
                MachineVariant = reader.GetString(3),
                Category = reader.GetString(4),
                OperationType = reader.GetString(5),
                Status = reader.GetString(6),
                CocktailId = reader.IsDBNull(7) ? null : reader.GetString(7),
                CocktailName = reader.IsDBNull(8) ? null : reader.GetString(8),
                ModeName = reader.IsDBNull(9) ? null : reader.GetString(9),
                TotalMilliliters = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                TotalDurationMs = reader.IsDBNull(11) ? null : reader.GetInt32(11),
                PayloadJson = reader.IsDBNull(12) ? null : reader.GetString(12)
            });
        }

        return events;
    }

    public void MarkEventsUploaded(IEnumerable<long> ids)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
            return;

        Initialize();

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (long id in idList)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                UPDATE machine_events
                SET uploaded_at_utc = $uploaded_at_utc
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$uploaded_at_utc", DateTime.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static void EnsureColumnExists(SqliteConnection connection, string tableName, string columnName, string definition)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                return;
        }
        reader.Close();

        using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};";
        alterCommand.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}

public class MachineEventRecord
{
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
    public string ProfileKey { get; init; } = string.Empty;
    public string MachineVariant { get; init; } = "unknown";
    public string Category { get; init; } = string.Empty;
    public string OperationType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? CocktailId { get; init; }
    public string? CocktailName { get; init; }
    public string? ModeName { get; init; }
    public int? TotalMilliliters { get; init; }
    public int? TotalDurationMs { get; init; }
    public string? PayloadJson { get; init; }
}

public sealed class PendingMachineEventRecord : MachineEventRecord
{
    public long Id { get; init; }
}

public sealed class MachineSyncStateRecord
{
    public string ProfileKey { get; init; } = string.Empty;
    public string MachineId { get; init; } = string.Empty;
    public int AppliedRemoteConfigVersion { get; init; }
    public DateTime? LastHeartbeatAtUtc { get; init; }
    public string? LastSyncError { get; init; }
}
