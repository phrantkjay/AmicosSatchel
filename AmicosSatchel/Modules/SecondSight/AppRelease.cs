// Written by publish-second-sight.ps1 in the XIV Map Project folder. Run that script after
// changing the map app; don't edit these values by hand.

namespace AmicosSatchel.Modules.SecondSight;

/// <summary>The Amico's Second Sight build this version of the satchel downloads.</summary>
internal static class AppRelease {
    /// <summary>GitHub account that hosts the release. Empty until the app is first published.</summary>
    public const string Owner = "phrantkjay";
    public const string Repository = "AmicosSatchel";
    public const string Tag = "second-sight-v1.0.0";
    public const string FileName = "AmicosSecondSight.exe";

    /// <summary>SHA-256 of the exe, as lowercase hex. A download that doesn't match is thrown away.</summary>
    public const string Sha256 = "077b9c688e3fc2e949216609d122315b286e5a541ec8e7d8656b3b1b98441ff0";

    public const long SizeBytes = 66828879;

    /// <summary>The message format (XIVMap.Shared Protocol.Version) this build of the map speaks.</summary>
    public const int ProtocolVersion = 16;

    public static bool IsPublished => Owner.Length > 0 && Tag.Length > 0 && Sha256.Length == 64;

    public static string DownloadUrl => $"https://github.com/{Owner}/{Repository}/releases/download/{Tag}/{FileName}";
}
