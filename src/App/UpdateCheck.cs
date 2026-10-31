using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ArnoA2.App;

/// <summary>A newer release and the page to get it from.</summary>
public sealed record AvailableUpdate(string Version, string Page)
{
    /// <summary>Where this release's files are.</summary>
    public string Downloads => Page.Replace("/releases/tag/", "/releases/download/", StringComparison.Ordinal);
}

/// <summary>
/// Asks GitHub for the newest release, once at start. Only that one request is made, and any
/// failure just means no prompt. GitHub answers only for
/// public repositories, so a private one never shows a prompt.
/// </summary>
public static partial class UpdateCheck
{
    const string Repository = "arnosantgaming-jpg/arno-a2-toolkit";

    [GeneratedRegex(@"^v?(\d+\.\d+\.\d+)$")]
    private static partial Regex Tag();

    /// <summary>The release in GitHub's answer when it is newer than the running version.</summary>
    public static AvailableUpdate? Newer(string json, string current)
    {
        try
        {
            using var answer = JsonDocument.Parse(json);
            if (answer.RootElement.ValueKind != JsonValueKind.Object || !answer.RootElement.TryGetProperty("tag_name", out var name) || name.ValueKind != JsonValueKind.String)
                return null;
            // The page is built here, so an odd answer can't send anyone elsewhere.
            var tag = Tag().Match(name.GetString()!);
            return tag.Success && Version.TryParse(current, out var running) && Version.Parse(tag.Groups[1].Value) > running
                ? new(tag.Groups[1].Value, $"https://github.com/{Repository}/releases/tag/v{tag.Groups[1].Value}")
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    public static async Task<AvailableUpdate?> Run(string current)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("arno-a2-toolkit", current));
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return Newer(await client.GetStringAsync($"https://api.github.com/repos/{Repository}/releases/latest"), current);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }
}
