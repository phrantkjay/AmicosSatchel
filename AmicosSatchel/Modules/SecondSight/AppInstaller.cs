using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using XIVMap.Shared;

namespace AmicosSatchel.Modules.SecondSight;

/// <summary>
/// Downloads the Amico's Second Sight app into the plugin's config folder, checks it against the
/// hash in <see cref="AppRelease"/>, and keeps it up to date. A new version is downloaded beside
/// the current one ("staged") and swapped in only while the map is closed, so a running map is
/// never interrupted. All network and disk work happens off the game thread.
/// </summary>
internal sealed class AppInstaller : IDisposable {
    public enum InstallState {
        NotPublished,
        NotInstalled,
        Downloading,
        /// <summary>Up to date.</summary>
        Installed,
        /// <summary>A newer map exists, but the installed one still talks to this satchel: it can be opened.</summary>
        UpdateAvailable,
        /// <summary>The installed map can't talk to this satchel (message format changed): it must be updated.</summary>
        UpdateRequired,
    }

    private static readonly HttpClient Http = CreateHttpClient();

    private readonly string folder;
    private volatile CancellationTokenSource? download;
    private long received;
    private long total;
    private volatile string? error;

    // What's on disk, re-checked every couple of seconds rather than every frame.
    private static readonly TimeSpan DiskCheckInterval = TimeSpan.FromSeconds(2);
    private readonly Stopwatch sinceDiskCheck = new();
    private bool exeExists;
    private string? installedHash;
    private int? installedProtocol;
    private bool stagedReady;

    public AppInstaller(string configDirectory) {
        folder = Path.Combine(configDirectory, "SecondSight");
    }

    /// <summary>Where the downloaded app lives.</summary>
    public string ExePath => Path.Combine(folder, AppRelease.FileName);

    /// <summary>A new version, downloaded and checked, waiting for the map to close.</summary>
    private string StagedPath => ExePath + ".new";

    /// <summary>"sha256" on the first line, the map's message format version on the second.</summary>
    private string VersionFile => Path.Combine(folder, "installed-version.txt");
    private string OldVersionFile => Path.Combine(folder, "installed-sha256.txt"); // before formats were recorded

    /// <summary>Why the last download failed, or null.</summary>
    public string? Error => error;

    /// <summary>0 to 1 while downloading.</summary>
    public float Progress {
        get {
            var size = Interlocked.Read(ref total);
            return size > 0 ? Math.Clamp((float)Interlocked.Read(ref received) / size, 0, 1) : 0;
        }
    }

    public long TotalBytes => Interlocked.Read(ref total) is > 0 and var size ? size : AppRelease.SizeBytes;

    /// <summary>True when the new version is downloaded and will be swapped in once the map closes.</summary>
    public bool UpdateStaged {
        get {
            CheckDisk();
            return stagedReady;
        }
    }

    public InstallState State {
        get {
            if (download is not null) return InstallState.Downloading;
            CheckDisk();
            if (!AppRelease.IsPublished) return exeExists ? InstallState.Installed : InstallState.NotPublished;
            if (!exeExists) return InstallState.NotInstalled;
            if (installedHash == AppRelease.Sha256) return InstallState.Installed;
            // An older map still works as long as it speaks the same message format as this satchel.
            return installedProtocol == Protocol.Version ? InstallState.UpdateAvailable : InstallState.UpdateRequired;
        }
    }

    /// <summary>Whether the downloaded copy is running, which locks the file against updating.</summary>
    public bool IsRunning() {
        try {
            var name = Path.GetFileNameWithoutExtension(AppRelease.FileName);
            return Process.GetProcessesByName(name).Any(p => {
                try { return string.Equals(p.MainModule?.FileName, ExePath, StringComparison.OrdinalIgnoreCase); }
                catch (Exception) { return false; }
            });
        } catch (Exception) {
            return false;
        }
    }

    /// <summary>
    /// Called every few seconds. With automatic updates on, downloads a new version in the
    /// background; either way, swaps a downloaded one in as soon as the map isn't running.
    /// </summary>
    public void Tick(bool autoUpdate) {
        if (download is not null) return;
        var state = State;
        if (stagedReady) {
            TryApplyStaged();
            return;
        }
        if (autoUpdate && state is InstallState.UpdateAvailable or InstallState.UpdateRequired && error is null)
            StartDownload();
    }

    /// <summary>
    /// Swaps in a staged update if the map is closed. Returns true when the installed map is now
    /// the newest one (also when there was nothing to swap).
    /// </summary>
    public bool TryApplyStaged() {
        CheckDisk();
        if (!stagedReady) return true;
        if (IsRunning()) return false;
        try {
            File.Move(StagedPath, ExePath, overwrite: true);
            File.WriteAllText(VersionFile, $"{AppRelease.Sha256}\n{AppRelease.ProtocolVersion}");
            TryDelete(OldVersionFile);
            Services.Log.Information($"Updated Amico's Second Sight to {AppRelease.Tag}.");
            return true;
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
            Services.Log.Debug(e, "Couldn't swap in the Second Sight update yet.");
            return false;
        } finally {
            sinceDiskCheck.Reset();
        }
    }

    /// <summary>Starts downloading in the background. Does nothing if one is already running.</summary>
    public void StartDownload() {
        if (download is not null || !AppRelease.IsPublished) return;

        error = null;
        Interlocked.Exchange(ref received, 0);
        Interlocked.Exchange(ref total, AppRelease.SizeBytes);
        var cts = new CancellationTokenSource();
        download = cts;
        _ = Task.Run(async () => {
            try {
                await DownloadAsync(cts.Token);
                sinceDiskCheck.Reset();
                TryApplyStaged(); // straight in if the map isn't open; otherwise when it closes
            } catch (OperationCanceledException) {
                error = null;
            } catch (Exception e) {
                Services.Log.Warning(e, "Couldn't download Amico's Second Sight.");
                error = e is HttpRequestException ? "Couldn't reach GitHub. Check your connection and try again." : e.Message;
            } finally {
                sinceDiskCheck.Reset(); // look at the disk again on the next frame
                download = null;
                cts.Dispose();
            }
        });
    }

    public void CancelDownload() {
        try { download?.Cancel(); } catch (ObjectDisposedException) { }
    }

    public void Dispose() => CancelDownload();

    /// <summary>Downloads to a temporary file, checks it, then stages it beside the current map.</summary>
    private async Task DownloadAsync(CancellationToken ct) {
        Directory.CreateDirectory(folder);
        var partial = ExePath + ".download";

        using (var response = await Http.GetAsync(AppRelease.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct)) {
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > 0 and var length) Interlocked.Exchange(ref total, length);

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true)) {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0) {
                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    Interlocked.Add(ref received, read);
                }
            }

            var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (actual != AppRelease.Sha256) {
                TryDelete(partial);
                throw new InvalidDataException("The download didn't match the expected file, so it was thrown away. Try again later.");
            }
        }

        File.Move(partial, StagedPath, overwrite: true);
        stagedChecked = AppRelease.Sha256; // just checked while downloading
        Services.Log.Information($"Downloaded Amico's Second Sight {AppRelease.Tag}; it goes in when the map is closed.");
    }

    private void CheckDisk() {
        if (sinceDiskCheck.IsRunning && sinceDiskCheck.Elapsed < DiskCheckInterval) return;
        sinceDiskCheck.Restart();

        exeExists = File.Exists(ExePath);
        installedHash = null;
        installedProtocol = null;
        try {
            if (File.Exists(VersionFile)) {
                var lines = File.ReadAllLines(VersionFile);
                installedHash = lines.ElementAtOrDefault(0)?.Trim();
                installedProtocol = int.TryParse(lines.ElementAtOrDefault(1), out var protocol) ? protocol : null;
            } else if (File.Exists(OldVersionFile)) {
                installedHash = File.ReadAllText(OldVersionFile).Trim(); // format unknown: treated as needing the update
            }
            // A staged file only counts if it's this release; a leftover from an older one is ignored.
            stagedReady = File.Exists(StagedPath) && installedHash != AppRelease.Sha256 && StagedMatches();
        } catch (IOException) {
            stagedReady = false;
        }
    }

    private string? stagedChecked;

    /// <summary>Whether the staged file is this release (hashed once per release, not every check).</summary>
    private bool StagedMatches() {
        if (stagedChecked == AppRelease.Sha256) return true;
        using var stream = File.OpenRead(StagedPath);
        var ok = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant() == AppRelease.Sha256;
        if (ok) stagedChecked = AppRelease.Sha256;
        return ok;
    }

    private static void TryDelete(string path) {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static HttpClient CreateHttpClient() {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AmicosSatchel");
        return client;
    }
}
