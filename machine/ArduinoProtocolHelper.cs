using System;
using System.Collections.Generic;
using System.Linq;
using DaevaMini.Config;
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
    /// <param name="pumpDuty">PWM duty per channel for the bottle pumps, or null for full power
    /// (see <see cref="FormatPumpSegments"/>).</param>
    public static string BuildActiveCommand((byte R, byte G, byte B) ledRgb, IReadOnlyDictionary<int, int> channelDurations,
        Func<int, byte>? pumpDuty = null)
        => $"ACTIVE, RGB:{ledRgb.R},{ledRgb.G},{ledRgb.B}, {FormatPumpSegments(channelDurations, pumpDuty)}";

    /// <summary>
    /// Builds the ACTIVE command line with a preset color name: ACTIVE, BASE:&lt;color&gt;, P1:&lt;ms&gt;, P2:&lt;ms&gt;, ...
    /// Pump segments are ordered by pump ID. At least one pump must have duration &gt; 0 (caller responsibility).
    /// </summary>
    /// <param name="pumpDuty">PWM duty per channel for the bottle pumps, or null for full power
    /// (see <see cref="FormatPumpSegments"/>).</param>
    public static string BuildActiveCommand(string ledColor, IReadOnlyDictionary<int, int> channelDurations,
        Func<int, byte>? pumpDuty = null)
        => $"ACTIVE, BASE:{NormalizeLedColor(ledColor)}, {FormatPumpSegments(channelDurations, pumpDuty)}";

    /// <summary>The firmware's own default drive, sent as no suffix at all.</summary>
    private const byte FullPumpDuty = 255;

    /// <summary>
    /// "P1:&lt;ms&gt;, P3:&lt;ms&gt;@&lt;duty&gt;, ..." - each bottle pump channel carries its own
    /// reduced duty. Keg channels never carry one: they are solenoid valves, and a valve on
    /// a partial duty can chatter or not open at all. At full duty nothing is appended, so
    /// the line is exactly what it was before power became adjustable.
    /// </summary>
    private static string FormatPumpSegments(IReadOnlyDictionary<int, int> channelDurations, Func<int, byte>? pumpDuty)
    {
        var pumpParts = channelDurations
            .Where(kvp => kvp.Value > 0)
            .OrderBy(kvp => kvp.Key)
            .Select(kvp =>
            {
                byte duty = pumpDuty != null && kvp.Key <= AppConfig.PumpChannelCount
                    ? pumpDuty(kvp.Key)
                    : FullPumpDuty;
                return duty is > 0 and < FullPumpDuty
                    ? $"P{kvp.Key}:{kvp.Value}@{duty}"
                    : $"P{kvp.Key}:{kvp.Value}";
            });
        return string.Join(", ", pumpParts);
    }

    /// <summary>
    /// Command that recolors the idle strips: TINT, RGB:&lt;r&gt;,&lt;g&gt;,&lt;b&gt;. Used to show
    /// which part of the machine is on screen. The firmware applies it only in WAIT,
    /// and holding a tint does not stop ACTIVE from working.
    /// </summary>
    public static string BuildTintCommand((byte R, byte G, byte B) ledRgb)
        => $"TINT, RGB:{ledRgb.R},{ledRgb.G},{ledRgb.B}";

    /// <summary>Drops the tint and returns the strips to the default idle color.</summary>
    public const string TintOffCommand = "TINT OFF";

    /// <summary>
    /// Opens a tap channel, or keeps one already open alive: TAP, RGB:&lt;r&gt;,&lt;g&gt;,&lt;b&gt;, P&lt;n&gt;.
    /// Deliberately carries no duration. The firmware only holds the valve open for a
    /// short window and expects this to be repeated, so that a host which stops
    /// running cannot leave a valve open behind it.
    /// </summary>
    public static string BuildTapCommand((byte R, byte G, byte B) ledRgb, int channel)
        => $"TAP, RGB:{ledRgb.R},{ledRgb.G},{ledRgb.B}, P{channel}";

    /// <summary>Closes the tap now and runs the normal end-of-pour LED sequence.</summary>
    public const string TapOffCommand = "TAP OFF";

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
    /// Parses the firmware response to a TAP or TAP OFF. Returns NoResponse for null/empty
    /// or unknown lines.
    /// </summary>
    public static TapResponse ParseTapResponse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return TapResponse.NoResponse;

        string trimmed = line.Trim();

        // "OK TAP OFF" has to be tested before "OK TAP", or it would match the prefix.
        if (trimmed.Equals("OK TAP OFF", StringComparison.OrdinalIgnoreCase))
            return TapResponse.ClosedOk;
        if (trimmed.Equals("OK TAP", StringComparison.OrdinalIgnoreCase))
            return TapResponse.Success;
        if (trimmed.Equals("ERR TAP COLOR", StringComparison.OrdinalIgnoreCase))
            return TapResponse.ErrColor;
        if (trimmed.Equals("ERR TAP PARAMS", StringComparison.OrdinalIgnoreCase))
            return TapResponse.ErrParams;
        if (trimmed.Equals("ERR TAP TIMEOUT", StringComparison.OrdinalIgnoreCase))
            return TapResponse.ErrTimeout;
        if (trimmed.Equals("IGNORED TAP", StringComparison.OrdinalIgnoreCase))
            return TapResponse.Ignored;

        return TapResponse.NoResponse;
    }

    /// <summary>
    /// Maps cocktail ingredients to pump channels.
    /// Returns a dictionary of channel number (1-based) to duration in milliseconds.
    /// Ingredients with no matching assignment are silently skipped.
    /// </summary>
    public static Dictionary<int, int> MapIngredientsToChannels(
        IReadOnlyList<CocktailIngredient> ingredients, string[] assignments, int msPerMl)
        => MapIngredientsToChannels(ingredients, assignments, _ => msPerMl);

    /// <summary>
    /// Same mapping, but the rate is asked per channel: a keg and a peristaltic pump
    /// do not share a flow rate.
    /// </summary>
    public static Dictionary<int, int> MapIngredientsToChannels(
        IReadOnlyList<CocktailIngredient> ingredients, string[] assignments, Func<int, int> msPerMlForChannel)
    {
        var channelDurations = new Dictionary<int, int>();
        foreach (var ingredient in ingredients)
        {
            for (int position = 0; position < assignments.Length; position++)
            {
                if (assignments[position].Equals(ingredient.Name, StringComparison.OrdinalIgnoreCase))
                {
                    int channel = position + 1;
                    int durationMs = ingredient.Milliliters * msPerMlForChannel(channel);
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

/// <summary>Result of sending a TAP or TAP OFF command (from firmware response).</summary>
public enum TapResponse
{
    /// <summary>OK TAP: the valve is open, or an open one was kept alive.</summary>
    Success,
    /// <summary>OK TAP OFF: closed on request.</summary>
    ClosedOk,
    ErrColor,
    ErrParams,
    /// <summary>ERR TAP TIMEOUT: the board's own ceiling closed the valve.</summary>
    ErrTimeout,
    /// <summary>IGNORED TAP: opening while not in WAIT, or closing with nothing open.</summary>
    Ignored,
    NoResponse
}
