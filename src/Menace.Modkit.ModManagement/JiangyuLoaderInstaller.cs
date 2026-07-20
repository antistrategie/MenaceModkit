using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Menace.Modkit.ModManagement;

/// <summary>
/// Downloads and installs the Jiangyu loader (a single <c>Jiangyu.Loader.dll</c>, the lean
/// "user" variant) from the antistrategie/jiangyu GitHub releases into <c>Mods/</c>.
/// Latest or a specific tagged version. The loader is a MelonMod, so MelonLoader must be
/// installed first (that is MelonLoader's own concern, handled elsewhere).
/// </summary>
public sealed class JiangyuLoaderInstaller
{
    private const string ReleasesApi = "https://api.github.com/repos/antistrategie/jiangyu/releases";
    private const string LoaderAssetName = "Jiangyu.Loader.dll";

    private readonly IModkitConfig _config;
    private readonly HttpClient _http;

    // One long-lived client for all instances (HttpClient is designed to be shared, and
    // per-instance clients were never disposed); a handler-injected instance (tests)
    // still gets its own.
    private static readonly HttpClient SharedHttp = CreateClient(null);

    public JiangyuLoaderInstaller(IModkitConfig? config = null, HttpMessageHandler? handler = null)
    {
        _config = config ?? ModkitConfig.Current;
        _http = handler is null ? SharedHttp : CreateClient(handler);
    }

    private static HttpClient CreateClient(HttpMessageHandler? handler)
    {
        var http = handler is null ? new HttpClient() : new HttpClient(handler);
        // GitHub requires a User-Agent.
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MenaceModManager", "1.0"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    public string? ModsPath =>
        string.IsNullOrEmpty(_config.GameInstallPath) ? null : Path.Combine(_config.GameInstallPath, "Mods");

    /// <summary>Path to the installed loader DLL if present, else null.</summary>
    public string? InstalledLoaderPath
    {
        get
        {
            var p = ModsPath is null ? null : Path.Combine(ModsPath, LoaderAssetName);
            return p != null && File.Exists(p) ? p : null;
        }
    }

    /// <summary>Available release versions (tag names), newest first.</summary>
    public async Task<IReadOnlyList<string>> ListVersionsAsync(CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync(ReleasesApi, ct).ConfigureAwait(false);
        return doc.RootElement.EnumerateArray()
            .Where(r => !GetBool(r, "draft"))
            .Select(r => GetString(r, "tag_name"))
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList()!;
    }

    /// <summary>
    /// Install a Jiangyu loader version (null/"latest" installs the newest release with a
    /// loader asset). Returns the installed path (<c>Mods/Jiangyu.Loader.dll</c>).
    /// </summary>
    public async Task<string> InstallAsync(string? version = null, CancellationToken ct = default)
    {
        var modsPath = ModsPath ?? throw new InvalidOperationException("Game install path is not set.");

        var downloadUrl = await ResolveLoaderUrlAsync(version, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                version is null or "latest"
                    ? "No Jiangyu release with a loader asset was found."
                    : $"Version '{version}' not found or has no loader asset.");

        Directory.CreateDirectory(modsPath);
        var dest = Path.Combine(modsPath, LoaderAssetName);

        var bytes = await _http.GetByteArrayAsync(downloadUrl, ct).ConfigureAwait(false);

        // Write to a temp file then move into place so a failed download can't corrupt an
        // existing loader.
        var temp = dest + ".download";
        await File.WriteAllBytesAsync(temp, bytes, ct).ConfigureAwait(false);
        File.Move(temp, dest, overwrite: true);

        return dest;
    }

    private async Task<string?> ResolveLoaderUrlAsync(string? version, CancellationToken ct)
    {
        using var doc = await GetJsonAsync(ReleasesApi, ct).ConfigureAwait(false);

        // Newest first, drafts excluded.
        var releases = doc.RootElement.EnumerateArray()
            .Where(r => !GetBool(r, "draft"))
            .ToList();

        IEnumerable<JsonElement> candidates =
            version is null || version.Equals("latest", StringComparison.OrdinalIgnoreCase)
                // For "latest": prefer stable releases, then fall back to any (incl. prereleases).
                ? releases.Where(r => !GetBool(r, "prerelease")).Concat(releases)
                : releases.Where(r => string.Equals(GetString(r, "tag_name"), version, StringComparison.OrdinalIgnoreCase));

        foreach (var release in candidates)
        {
            var url = FindLoaderAssetUrl(release);
            if (url != null)
                return url;
        }

        return null;
    }

    private static string? FindLoaderAssetUrl(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var asset in assets.EnumerateArray())
        {
            if (string.Equals(GetString(asset, "name"), LoaderAssetName, StringComparison.OrdinalIgnoreCase))
                return GetString(asset, "browser_download_url");
        }
        return null;
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        using var stream = await _http.GetStreamAsync(url, ct).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
    }

    private static string? GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool GetBool(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
