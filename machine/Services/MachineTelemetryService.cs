using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DaevaMini.Models;

namespace DaevaMini.Services;

public sealed class MachineTelemetryService
{
    private static MachineTelemetryService? _instance;
    private static readonly object LockObject = new();

    public static MachineTelemetryService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (LockObject)
                {
                    _instance ??= new MachineTelemetryService();
                }
            }

            return _instance;
        }
    }

    private MachineTelemetryService()
    {
    }

    public void RecordConfigEvent(string profileKey, string status, string source, object? details = null)
    {
        Record(new MachineEventRecord
        {
            ProfileKey = profileKey,
            MachineVariant = MachineProfileHelper.ResolveVariant(profileKey),
            Category = "config",
            OperationType = "config",
            Status = status,
            PayloadJson = SerializePayload(new
            {
                source,
                details
            })
        });
    }

    public void RecordDispenseEvent(
        string profileKey,
        string status,
        Cocktail cocktail,
        string? modeName,
        IReadOnlyDictionary<int, int>? channelDurations,
        object? details = null)
    {
        int totalDurationMs = channelDurations?.Values.DefaultIfEmpty(0).Max() ?? 0;
        int totalMilliliters = cocktail.Ingredients.Sum(i => i.Milliliters);

        Record(new MachineEventRecord
        {
            ProfileKey = profileKey,
            MachineVariant = MachineProfileHelper.ResolveVariant(profileKey),
            Category = "operation",
            OperationType = "dispense",
            Status = status,
            CocktailId = cocktail.Id,
            CocktailName = cocktail.Title,
            ModeName = modeName,
            TotalMilliliters = totalMilliliters,
            TotalDurationMs = totalDurationMs,
            PayloadJson = SerializePayload(new
            {
                details,
                channelDurations,
                ingredients = cocktail.Ingredients.Select(i => new
                {
                    i.Name,
                    i.Milliliters
                })
            })
        });
    }

    public void RecordMaintenanceEvent(
        string profileKey,
        string operationType,
        string status,
        IReadOnlyList<int> channels,
        int durationMs,
        object? details = null)
    {
        Record(new MachineEventRecord
        {
            ProfileKey = profileKey,
            MachineVariant = MachineProfileHelper.ResolveVariant(profileKey),
            Category = "operation",
            OperationType = operationType,
            Status = status,
            TotalDurationMs = durationMs,
            PayloadJson = SerializePayload(new
            {
                details,
                channels
            })
        });
    }

    private static void Record(MachineEventRecord record)
    {
        try
        {
            LocalMachineStore.Instance.RecordEvent(record);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MachineTelemetryService] Failed to record event: {ex.Message}");
        }
    }

    private static string SerializePayload(object? payload)
    {
        return JsonSerializer.Serialize(payload ?? new { });
    }
}
