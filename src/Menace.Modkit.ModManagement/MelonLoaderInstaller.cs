using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Menace.Modkit.App.Services; // PathValidator (kept its original namespace when extracted)
using SharpCompress.Archives;
using SharpCompress.Common;

namespace Menace.Modkit.ModManagement;

/// <summary>
/// Downloads and installs MelonLoader from the LavaGang/MelonLoader GitHub releases by
/// extracting the Windows x64 package straight into the game directory (the official
/// "unzip into the game folder" install). MENACE is a Windows build — native or run via
/// Proton on Linux — so only <c>MelonLoader.x64.zip</c> is used.
/// </summary>
public sealed class MelonLoaderInstaller
{
    private const string ReleasesApi = "https://api.github.com/repos/LavaGang/MelonLoader/releases";
    private const string AssetName = "MelonLoader.x64.zip";

    // Versions known to be incompatible with this game/toolchain.
    private static readonly HashSet<string> UnsupportedVersions = new(StringComparer.OrdinalIgnoreCase)
    {
        "0.9.1",
    };

    private readonly IModkitConfig _config;
    private readonly HttpClient _http;

    // One long-lived client for all instances (HttpClient is designed to be shared, and
    // per-instance clients were never disposed); a handler-injected instance (tests)
    // still gets its own.
    private static readonly HttpClient SharedHttp = CreateClient(null);

    public MelonLoaderInstaller(IModkitConfig? config = null, HttpMessageHandler? handler = null)
    {
        _config = config ?? ModkitConfig.Current;
        _http = handler is null ? SharedHttp : CreateClient(handler);
    }

    private static HttpClient CreateClient(HttpMessageHandler? handler)
    {
        var http = handler is null ? new HttpClient() : new HttpClient(handler);
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MenaceModManager", "1.0"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    public string? GameInstallPath => string.IsNullOrEmpty(_config.GameInstallPath) ? null : _config.GameInstallPath;

    /// <summary>Available MelonLoader versions (tag names), newest first, minus unsupported ones.</summary>
    public async Task<IReadOnlyList<string>> ListVersionsAsync(CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync(ReleasesApi, ct).ConfigureAwait(false);
        return doc.RootElement.EnumerateArray()
            .Where(r => !GetBool(r, "draft"))
            .Select(r => GetString(r, "tag_name"))
            .Where(t => !string.IsNullOrEmpty(t) && !UnsupportedVersions.Contains(StripV(t!)))
            .ToList()!;
    }

    /// <summary>
    /// Download a MelonLoader version (null/"latest" = newest release) and extract it into
    /// the game directory. Returns the game directory path.
    /// </summary>
    public async Task<string> InstallAsync(string? version = null, CancellationToken ct = default)
    {
        var gameDir = GameInstallPath ?? throw new InvalidOperationException("Game install path is not set.");

        if (version is not null && version is not "latest" && UnsupportedVersions.Contains(StripV(version)))
            throw new InvalidOperationException($"MelonLoader {version} is not supported.");

        var url = await ResolveAssetUrlAsync(version, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                version is null or "latest"
                    ? $"No MelonLoader release with a {AssetName} asset was found."
                    : $"MelonLoader {version} not found or has no {AssetName} asset.");

        var bytes = await _http.GetByteArrayAsync(url, ct).ConfigureAwait(false);

        var tempZip = Path.Combine(Path.GetTempPath(), "melonloader-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            await File.WriteAllBytesAsync(tempZip, bytes, ct).ConfigureAwait(false);
            ExtractInto(tempZip, gameDir);
        }
        finally
        {
            try { File.Delete(tempZip); } catch { /* best effort */ }
        }

        return gameDir;
    }

    private async Task<string?> ResolveAssetUrlAsync(string? version, CancellationToken ct)
    {
        using var doc = await GetJsonAsync(ReleasesApi, ct).ConfigureAwait(false);

        var releases = doc.RootElement.EnumerateArray().Where(r => !GetBool(r, "draft")).ToList();

        IEnumerable<JsonElement> candidates =
            version is null || version.Equals("latest", StringComparison.OrdinalIgnoreCase)
                ? releases.Where(r => !GetBool(r, "prerelease")).Concat(releases)
                : releases.Where(r => string.Equals(GetString(r, "tag_name"), version, StringComparison.OrdinalIgnoreCase));

        foreach (var release in candidates)
        {
            if (release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (string.Equals(GetString(asset, "name"), AssetName, StringComparison.OrdinalIgnoreCase))
                        return GetString(asset, "browser_download_url");
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Extract every entry into <paramref name="destDir"/>, preserving structure,
    /// overwriting. The archive is fully extracted to a temp dir first so a corrupt
    /// download fails before any game file is touched — extracting straight over the
    /// game dir would leave a broken mixed-version MelonLoader behind.
    /// </summary>
    private static void ExtractInto(string zipPath, string destDir)
    {
        var staging = Path.Combine(Path.GetTempPath(), "ml-extract-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var archive = ArchiveFactory.Open(zipPath))
            {
                foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                {
                    if (string.IsNullOrEmpty(entry.Key))
                        continue;

                    var destPath = PathValidator.ValidateArchiveEntryPath(staging, entry.Key);
                    var dir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);

                    entry.WriteToFile(destPath, new ExtractionOptions { ExtractFullPath = false, Overwrite = true });
                }
            }

            Directory.CreateDirectory(destDir);
            CopyTreeOver(staging, destDir);
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch { /* best effort */ }
        }
    }

    private static void CopyTreeOver(string source, string dest)
    {
        Directory.CreateDirectory(dest);

        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);

        foreach (var dir in Directory.GetDirectories(source))
            CopyTreeOver(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        using var stream = await _http.GetStreamAsync(url, ct).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
    }

    private static string StripV(string tag) => tag.TrimStart('v', 'V');

    private static string? GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool GetBool(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
