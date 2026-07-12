using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DaevaMini.Services;

public sealed class MachineSyncService : IDisposable
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(25);

    private readonly HttpClient _httpClient;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _syncLoopTask;
    private string? _profileKey;
    private string? _machineId;
    private string? _machineSecret;

    private static MachineSyncService? _instance;
    private static readonly object LockObject = new();

    public static MachineSyncService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (LockObject)
                {
                    _instance ??= new MachineSyncService();
                }
            }

            return _instance;
        }
    }

    private MachineSyncService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public void Start(string profileKey)
    {
        if (_syncLoopTask != null)
            return;

        _profileKey = profileKey;
        _machineId = MachineProfileHelper.ResolveMachineId(profileKey);
        _machineSecret = MachineProfileHelper.ResolveMachineSecret();
        _httpClient.BaseAddress = new Uri(MachineProfileHelper.ResolveBackendBaseUrl());

        LocalMachineStore.Instance.SeedSyncState(profileKey, _machineId);

        _cancellationTokenSource = new CancellationTokenSource();
        _syncLoopTask = Task.Run(() => RunLoopAsync(_cancellationTokenSource.Token));
    }

    public void Stop()
    {
        if (_cancellationTokenSource == null)
            return;

        if (!string.IsNullOrWhiteSpace(_profileKey) && !string.IsNullOrWhiteSpace(_machineId))
            SendOfflineHeartbeat(_profileKey!, _machineId!);

        _cancellationTokenSource.Cancel();

        try
        {
            _syncLoopTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is TaskCanceledException or OperationCanceledException))
        {
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _syncLoopTask = null;
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private void SendOfflineHeartbeat(string profileKey, string machineId)
    {
        try
        {
            MachineSyncStateRecord syncState = LocalMachineStore.Instance.GetSyncState(profileKey);
            var payload = new
            {
                machineId,
                isOnline = false,
                healthStatus = "RED",
                errors = new[] { "machine-shutdown" },
                timestamp = DateTime.UtcNow.ToString("O"),
                appliedConfigVersion = syncState.AppliedRemoteConfigVersion
            };

            using HttpRequestMessage request = CreateJsonRequest(HttpMethod.Post, "/api/machine-status", payload);
            using HttpResponseMessage response = _httpClient
                .SendAsync(request)
                .GetAwaiter()
                .GetResult();

            if (response.IsSuccessStatusCode)
                LocalMachineStore.Instance.UpdateHeartbeat(profileKey);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MachineSyncService] Failed to send offline heartbeat: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Stop();
        _httpClient.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TimeSpan delay = DefaultPollInterval;

            try
            {
                if (string.IsNullOrWhiteSpace(_profileKey) || string.IsNullOrWhiteSpace(_machineId))
                    return;

                await SendHeartbeatAsync(_profileKey, _machineId, cancellationToken);
                LocalMachineStore.Instance.UpdateHeartbeat(_profileKey);

                await UploadPendingEventsAsync(_profileKey, _machineId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrWhiteSpace(_profileKey))
                    LocalMachineStore.Instance.RecordSyncError(_profileKey, ex.Message);

                Console.WriteLine($"[MachineSyncService] Sync error: {ex.Message}");
            }

            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task SendHeartbeatAsync(
        string profileKey,
        string machineId,
        CancellationToken cancellationToken)
    {
        MachineSyncStateRecord syncState = LocalMachineStore.Instance.GetSyncState(profileKey);
        var payload = new
        {
            machineId,
            isOnline = true,
            healthStatus = MachineRuntimeState.Instance.IsBusy ? "YELLOW" : "GREEN",
            errors = Array.Empty<string>(),
            timestamp = DateTime.UtcNow.ToString("O"),
            runtimeStatus = MachineRuntimeState.Instance.Status,
            currentOperation = MachineRuntimeState.Instance.CurrentOperation,
            appliedConfigVersion = syncState.AppliedRemoteConfigVersion
        };

        using HttpRequestMessage request = CreateJsonRequest(HttpMethod.Post, "/api/machine-status", payload);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task UploadPendingEventsAsync(
        string profileKey,
        string machineId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PendingMachineEventRecord> pendingEvents = LocalMachineStore.Instance.GetPendingEvents(profileKey);
        if (pendingEvents.Count == 0)
            return;

        foreach (PendingMachineEventRecord pendingEvent in pendingEvents)
        {
            if (!IsGestionalePourEvent(pendingEvent))
            {
                LocalMachineStore.Instance.MarkEventsUploaded(new[] { pendingEvent.Id });
                continue;
            }

            var payload = new
            {
                machineId,
                clientEventId = pendingEvent.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                cocktailName = pendingEvent.CocktailName,
                liquidPortions = BuildLiquidPortions(pendingEvent),
                timestamp = pendingEvent.OccurredAtUtc.ToString("O"),
                localEventId = pendingEvent.Id,
                machineVariant = pendingEvent.MachineVariant,
                totalDurationMs = pendingEvent.TotalDurationMs
            };

            using HttpRequestMessage request = CreateJsonRequest(HttpMethod.Post, "/api/machine-events", payload);
            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            LocalMachineStore.Instance.MarkEventsUploaded(new[] { pendingEvent.Id });
        }
    }

    private HttpRequestMessage CreateJsonRequest<T>(HttpMethod method, string requestUri, T payload)
    {
        var request = new HttpRequestMessage(method, requestUri)
        {
            Content = JsonContent.Create(payload)
        };

        if (!string.IsNullOrWhiteSpace(_machineSecret))
            request.Headers.Add("x-machine-secret", _machineSecret);

        return request;
    }

    private static bool IsGestionalePourEvent(PendingMachineEventRecord record)
    {
        return string.Equals(record.Category, "operation", StringComparison.OrdinalIgnoreCase)
            && string.Equals(record.OperationType, "dispense", StringComparison.OrdinalIgnoreCase)
            && string.Equals(record.Status, "completed", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(record.CocktailName);
    }

    private static object[] BuildLiquidPortions(PendingMachineEventRecord record)
    {
        JsonElement payload = DeserializePayload(record.PayloadJson);
        if (payload.TryGetProperty("ingredients", out JsonElement ingredients) &&
            ingredients.ValueKind == JsonValueKind.Array)
        {
            var portions = new List<object>();
            foreach (JsonElement ingredient in ingredients.EnumerateArray())
            {
                string? name = TryGetStringProperty(ingredient, "Name") ?? TryGetStringProperty(ingredient, "name");
                int? milliliters = TryGetIntProperty(ingredient, "Milliliters") ?? TryGetIntProperty(ingredient, "milliliters");

                if (!string.IsNullOrWhiteSpace(name) && milliliters.HasValue)
                {
                    portions.Add(new
                    {
                        name,
                        amountMl = milliliters.Value
                    });
                }
            }

            if (portions.Count > 0)
                return portions.ToArray();
        }

        return new[]
        {
            new
            {
                name = record.CocktailName ?? "Drink",
                amountMl = record.TotalMilliliters ?? 0
            }
        };
    }

    private static JsonElement DeserializePayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return JsonSerializer.Deserialize<JsonElement>("{}");

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(payloadJson);
        }
        catch
        {
            return JsonSerializer.Deserialize<JsonElement>("{}");
        }
    }

    private static string? TryGetStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static int? TryGetIntProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
            return null;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int value))
            return value;

        return null;
    }
}
