using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DaevaMini.Config;

namespace DaevaMini.Services;

public sealed class MachineSyncService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(15);

    private readonly HttpClient _httpClient;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _syncLoopTask;
    private string? _profileKey;
    private string? _machineId;
    private bool _hasPendingConfigRefresh;

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
                status = "offline",
                appliedConfigVersion = syncState.AppliedRemoteConfigVersion,
                currentOperation = (string?)null
            };

            using HttpResponseMessage response = _httpClient
                .PostAsJsonAsync($"/api/machines/{machineId}/heartbeat", payload)
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

                var heartbeat = await SendHeartbeatAsync(_profileKey, _machineId, cancellationToken);
                LocalMachineStore.Instance.UpdateHeartbeat(_profileKey);

                if (heartbeat.ShouldRefreshConfig)
                    _hasPendingConfigRefresh = true;

                if (_hasPendingConfigRefresh && !MachineRuntimeState.Instance.IsBusy)
                    await RefreshDesiredConfigAsync(_profileKey, _machineId, cancellationToken);

                await UploadPendingEventsAsync(_profileKey, _machineId, cancellationToken);

                delay = TimeSpan.FromSeconds(Math.Max(5, heartbeat.PollIntervalSeconds));
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

    private async Task<MachineHeartbeatResponseDto> SendHeartbeatAsync(
        string profileKey,
        string machineId,
        CancellationToken cancellationToken)
    {
        MachineSyncStateRecord syncState = LocalMachineStore.Instance.GetSyncState(profileKey);
        var payload = new
        {
            status = MachineRuntimeState.Instance.Status,
            appliedConfigVersion = syncState.AppliedRemoteConfigVersion,
            currentOperation = MachineRuntimeState.Instance.CurrentOperation
        };

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            $"/api/machines/{machineId}/heartbeat",
            payload,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        MachineHeartbeatResponseDto? result = await response.Content.ReadFromJsonAsync<MachineHeartbeatResponseDto>(JsonOptions, cancellationToken);
        return result ?? new MachineHeartbeatResponseDto();
    }

    private async Task RefreshDesiredConfigAsync(
        string profileKey,
        string machineId,
        CancellationToken cancellationToken)
    {
        MachineSyncStateRecord syncState = LocalMachineStore.Instance.GetSyncState(profileKey);

        DesiredConfigResponseDto? desiredConfig = await _httpClient.GetFromJsonAsync<DesiredConfigResponseDto>(
            $"/api/machines/{machineId}/desired-config",
            JsonOptions,
            cancellationToken);

        if (desiredConfig?.Machine == null || desiredConfig.Config == null)
            return;

        if (desiredConfig.Machine.DesiredConfigVersion <= syncState.AppliedRemoteConfigVersion)
        {
            _hasPendingConfigRefresh = false;
            return;
        }

        AppConfigService.Instance.ApplyConfig(desiredConfig.Config, $"remote-sync-v{desiredConfig.Machine.DesiredConfigVersion}");
        LocalMachineStore.Instance.UpdateAppliedRemoteConfigVersion(profileKey, desiredConfig.Machine.DesiredConfigVersion);
        _hasPendingConfigRefresh = false;

        var appliedPayload = new
        {
            appliedConfigVersion = desiredConfig.Machine.DesiredConfigVersion,
            status = MachineRuntimeState.Instance.Status,
            details = new Dictionary<string, object?>
            {
                ["source"] = "machine-sync",
                ["localConfigVersion"] = LocalMachineStore.Instance.GetConfigVersion(profileKey)
            }
        };

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            $"/api/machines/{machineId}/config-applied",
            appliedPayload,
            cancellationToken);
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

        var payload = new
        {
            events = pendingEvents.Select(e => new
            {
                category = e.Category,
                operationType = e.OperationType,
                status = e.Status,
                occurredAt = e.OccurredAtUtc,
                cocktailId = e.CocktailId,
                cocktailName = e.CocktailName,
                modeName = e.ModeName,
                totalMilliliters = e.TotalMilliliters,
                totalDurationMs = e.TotalDurationMs,
                payload = DeserializePayload(e.PayloadJson)
            }).ToArray()
        };

        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            $"/api/machines/{machineId}/telemetry",
            payload,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        LocalMachineStore.Instance.MarkEventsUploaded(pendingEvents.Select(e => e.Id));
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

    private sealed class MachineHeartbeatResponseDto
    {
        public bool ShouldRefreshConfig { get; set; }
        public int PollIntervalSeconds { get; set; } = 15;
    }

    private sealed class DesiredConfigResponseDto
    {
        public MachineSummaryDto? Machine { get; set; }
        public AppConfig? Config { get; set; }
    }

    private sealed class MachineSummaryDto
    {
        public int DesiredConfigVersion { get; set; }
    }
}
