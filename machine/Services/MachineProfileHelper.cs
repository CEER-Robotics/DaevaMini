using System;

namespace DaevaMini.Services;

public static class MachineProfileHelper
{
    public static string ResolveMachineId(string? profileKey)
    {
        string? overrideMachineId = Environment.GetEnvironmentVariable("DAEVA_MACHINE_ID");
        if (!string.IsNullOrWhiteSpace(overrideMachineId))
            return overrideMachineId.Trim();

        return ResolveVariant(profileKey) switch
        {
            "mini" => "daeva-mini-01",
            "max" => "daeva-max-01",
            _ => "daeva-machine-unknown"
        };
    }

    public static string ResolveBackendBaseUrl()
    {
        string? overrideUrl = Environment.GetEnvironmentVariable("DAEVA_BACKEND_URL");
        return string.IsNullOrWhiteSpace(overrideUrl)
            ? "http://127.0.0.1:8000"
            : overrideUrl.Trim().TrimEnd('/');
    }

    public static string ResolveVariant(string? profileKey)
    {
        if (string.IsNullOrWhiteSpace(profileKey))
            return "unknown";

        return profileKey.Contains("mini", StringComparison.OrdinalIgnoreCase)
            ? "mini"
            : profileKey.Contains("max", StringComparison.OrdinalIgnoreCase)
                ? "max"
                : "unknown";
    }
}
