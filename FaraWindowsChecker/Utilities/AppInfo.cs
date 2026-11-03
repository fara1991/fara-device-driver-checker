namespace FaraWindowsChecker.Utilities;

using System.Reflection;

/// <summary>
/// アプリの名前とバージョン。バージョンは、ビルド時に埋め込まれた値（リリースでは、タグ v1.2.3 の 1.2.3）を読む。
/// 手元のビルドでは、既定の「0.0.0-dev」になり、「開発版」と表示する。
/// </summary>
public static class AppInfo
{
    public const string Name = "FaraWindowsChecker";

    /// <summary>バージョンを指定せずにビルドしたとき（手元のビルド）の値。csproj の Version と同じにする。</summary>
    public const string DevelopmentVersion = "0.0.0-dev";

    private const int ShortCommitLength = 7;

    /// <summary>「1.2.3」のような版の番号。</summary>
    public static string Version { get; } = ParseInformationalVersion(ReadInformationalVersion()).Version;

    /// <summary>ビルドしたコミット（先頭の7文字）。分からないときは null。</summary>
    public static string? Commit { get; } = ParseInformationalVersion(ReadInformationalVersion()).Commit;

    /// <summary>画面に表示するバージョン。例:「v1.2.3（コミット: abc1234）」「開発版（コミット: abc1234）」</summary>
    public static string DisplayVersion { get; } = FormatDisplayVersion(Version, Commit);

    /// <summary>
    /// 「1.2.3+abcdef0123…」の形のバージョン情報を、版の番号とコミットに分ける。
    /// 読めないときは、開発版として扱う。
    /// </summary>
    public static (string Version, string? Commit) ParseInformationalVersion(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion)) return (DevelopmentVersion, null);

        var parts = informationalVersion.Trim().Split('+', 2);
        var version = string.IsNullOrWhiteSpace(parts[0]) ? DevelopmentVersion : parts[0].Trim();
        var commit = parts.Length > 1 && parts[1].Length > 0
            ? parts[1][..Math.Min(ShortCommitLength, parts[1].Length)]
            : null;
        return (version, commit);
    }

    public static string FormatDisplayVersion(string version, string? commit)
    {
        var label = version == DevelopmentVersion ? "開発版" : $"v{version}";
        return commit == null ? label : $"{label}（コミット: {commit}）";
    }

    private static string? ReadInformationalVersion()
        => typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
}
