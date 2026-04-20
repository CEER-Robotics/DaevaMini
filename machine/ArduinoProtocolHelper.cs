using System;
using System.Collections.Generic;
using System.Linq;
using DaevaMini.Models;

namespace DaevaMini;

/// <summary>
/// Helpers for the Arduino serial command protocol (ACTIVE, responses).
/// Matches arduino/main/SERIAL_PROTOCOL.md and ProjectConfig.h preset colors.
/// </summary>
public static class ArduinoProtocolHelper
{
    /// <summary>Valid preset names for ACTIVE BASE/COLOR (firmware accepts case-insensitive; we normalize to this).</summary>
    private static readonly HashSet<string> ValidLedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        "ORANGE", "RED", "GREEN", "BLUE", "CYAN", "MAGENTA", "YELLOW", "WHITE", "WARM_WHITE", "PURPLE"
    };

    private const string DefaultLedColor = "ORANGE";

    /// <summary>
    /// Normalizes and validates LedColor for ACTIVE. Spaces and dashes become underscore; invalid/missing returns default.
    /// </summary>
    public static string NormalizeLedColor(string? ledColor)
    {
        if (string.IsNullOrWhiteSpace(ledColor))
            return DefaultLedColor;

        string normalized = ledColor.Trim()
            .Replace(' ', '_')
            .Replace('-', '_')
            .ToUpperInvariant();

        return ValidLedColors.Contains(normalized) ? normalized : DefaultLedColor;
    }

    /// <summary>
    /// Builds the ACTIVE command line with RGB: ACTIVE, RGB:&lt;r&gt;,&lt;g&gt;,&lt;b&gt;, P1:&lt;ms&gt;, ...
    /// Pump segments are ordered by pump ID. At least one pump must have duration &gt; 0 (caller responsibility).
    /// </summary>
    public static string BuildActiveCommand((byte R, byte G, byte B) ledRgb, IReadOnlyDictionary<int, int> channelDurations)
    {
        var pumpParts = channelDurations
            .Where(kvp => kvp.Value > 0)
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => $"P{kvp.Key}:{kvp.Value}");
        string pumpSegment = string.Join(", ", pumpParts);
        return $"ACTIVE, RGB:{ledRgb.R},{ledRgb.G},{ledRgb.B}, {pumpSegment}";
    }

    /// <summary>
    /// Builds the ACTIVE command line with a preset color name: ACTIVE, BASE:&lt;color&gt;, P1:&lt;ms&gt;, P2:&lt;ms&gt;, ...
    /// Pump segments are ordered by pump ID. At least one pump must have duration &gt; 0 (caller responsibility).
    /// </summary>
    public static string BuildActiveCommand(string ledColor, IReadOnlyDictionary<int, int> channelDurations)
    {
        string color = NormalizeLedColor(ledColor);
        var pumpParts = channelDurations
            .Where(kvp => kvp.Value > 0)
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => $"P{kvp.Key}:{kvp.Value}");
        string pumpSegment = string.Join(", ", pumpParts);
        return $"ACTIVE, BASE:{color}, {pumpSegment}";
    }

    /// <summary>
    /// Parses the firmware response after sending ACTIVE. Returns NoResponse for null/empty or unknown lines.
    /// </summary>
    public static ActivateResponse ParseActivateResponse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return ActivateResponse.NoResponse;

        string trimmed = line.Trim();
        if (trimmed.Equals("OK ACTIVE", StringComparison.OrdinalIgnoreCase))
            return ActivateResponse.Success;
        if (trimmed.Equals("ERR ACTIVE COLOR", StringComparison.OrdinalIgnoreCase))
            return ActivateResponse.ErrColor;
        if (trimmed.Equals("ERR ACTIVE PARAMS", StringComparison.OrdinalIgnoreCase))
            return ActivateResponse.ErrParams;
        if (trimmed.Equals("IGNORED ACTIVE", StringComparison.OrdinalIgnoreCase))
            return ActivateResponse.Ignored;

        return ActivateResponse.NoResponse;
    }

    /// <summary>
    /// Maps cocktail ingredients to pump channels.
    /// Returns a dictionary of channel number (1-based) to duration in milliseconds.
    /// Ingredients with no matching assignment are silently skipped.
    /// </summary>
    public static Dictionary<int, int> MapIngredientsToChannels(
        IReadOnlyList<CocktailIngredient> ingredients, string[] assignments, int msPerMl)
    {
        var channelDurations = new Dictionary<int, int>();
        foreach (var ingredient in ingredients)
        {
            for (int position = 0; position < assignments.Length; position++)
            {
                if (assignments[position].Equals(ingredient.Name, StringComparison.OrdinalIgnoreCase))
                {
                    int channel = position + 1;
                    int durationMs = ingredient.Milliliters * msPerMl;
                    if (channelDurations.ContainsKey(channel))
                        channelDurations[channel] += durationMs;
                    else
                        channelDurations[channel] = durationMs;
                    break;
                }
            }
        }
        return channelDurations;
    }
}

/// <summary>Result of sending an ACTIVE command (from firmware response).</summary>
public enum ActivateResponse
{
    Success,
    ErrColor,
    ErrParams,
    Ignored,
    NoResponse
}
