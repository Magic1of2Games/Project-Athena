using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace SyndicateLauncher.Services;

/// <summary>One season's progress of a profile, as the backend reports it.</summary>
public sealed record SeasonProgress(int Season, bool Active, int Level, long Xp, long XpToNext, int Tier);

/// <summary>The backend's answer to the progress list: the active season, the XP limit per request and every season.</summary>
public sealed record ProgressOverview(int ActiveSeason, long MaxXp, List<SeasonProgress> Seasons);

/// <summary>The state of a season right after XP was added to it.</summary>
public sealed record XpAddResult(int Season, int Level, long Xp, int Tier, bool Maxed);

/// <summary>
/// Launcher admin panel for in-game progress (Views/ProgressAdminWindow.cs). Talks to the backend routes
/// /launcher/admin/progress and /launcher/admin/xp (SyndicateClassic/backend/src/Routes/launcher.js), signed in as
/// the launcher profile. The backend does the season maths with the same tables a match uses.
/// </summary>
public static class ProgressAdmin
{
    /// <summary>Backend season numbers: 2-10 are Chapter 1 (10 = Season X), 11 and 12 are Chapter 2.</summary>
    public static string SeasonName(int season) => season switch
    {
        10 => "Season X",
        11 => "Chapter 2 · Season 1",
        12 => "Chapter 2 · Season 2",
        _ => "Season " + season
    };

    public static async Task<ProgressOverview> LoadAsync(string profile)
    {
        string json = await SyndicateBackend.PostAsPlayerAsync(profile, "launcher/admin/progress", new { });
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var seasons = new List<SeasonProgress>();
        if (root.TryGetProperty("seasons", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var item in list.EnumerateArray())
                seasons.Add(new SeasonProgress(
                    (int)Number(item, "season"), Flag(item, "active"), (int)Number(item, "level"),
                    Number(item, "xp"), Number(item, "xpToNext"), (int)Number(item, "tier")));
        return new ProgressOverview((int)Number(root, "active"), Math.Max(1, Number(root, "maxXp")), seasons);
    }

    public static async Task<XpAddResult> AddXpAsync(string profile, int season, long amount)
    {
        string json = await SyndicateBackend.PostAsPlayerAsync(profile, "launcher/admin/xp", new { season, amount });
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new XpAddResult((int)Number(root, "season"), (int)Number(root, "level"), Number(root, "xp"),
            (int)Number(root, "tier"), Flag(root, "maxed"));
    }

    private static long Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long n) ? n : 0;

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
