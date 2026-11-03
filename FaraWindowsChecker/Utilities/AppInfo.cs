namespace FaraWindowsChecker.Utilities;

using System.Reflection;

/// <summary>
/// アプリの名前とバージョン。バージョンは、ビルド時に埋め込まれた値（リリースでは、タグ v1.2.3 の 1.2.3）を読む。
/// 手元のビルドは、既定の「0.0.0-dev」になり、「開発版」と表示する。
/// コミットの番号は、アプリに残さない（csproj の IncludeSourceRevisionInInformationalVersion を false にしている）。
/// </summary>
public static class AppInfo
{
    public const string Name = "FaraWindowsChecker";

    /// <summary>バージョンを指定せずにビルドしたとき（手元のビルド）の値。csproj の Version と同じにする。</summary>
    public const string DevelopmentVersion = "0.0.0-dev";

    /// <summary>「1.2.3」のような版の番号。</summary>
    public static string Version { get; } = ParseInformationalVersion(ReadInformationalVersion());

    /// <summary>画面に表示するバージョン。例:「v1.2.3」「開発版」</summary>
    public static string DisplayVersion { get; } = FormatDisplayVersion(Version);

    /// <summary>
    /// バージョン情報から、版の番号だけを取り出す。読めないときは、開発版として扱う。
    /// 「+」以降（ビルドのメタデータ）は、使わない。
    /// </summary>
    public static string ParseInformationalVersion(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion)) return DevelopmentVersion;

        var version = informationalVersion.Split('+', 2)[0].Trim();
        return version.Length == 0 ? DevelopmentVersion : version;
    }

    /// <summary>表示用のバージョン。リリースは先頭に「v」を付ける（v1.2.3）。開発版（0.0.0-dev）は「開発版」と表示する。</summary>
    public static string FormatDisplayVersion(string version)
        => version == DevelopmentVersion ? "開発版" : $"v{version}";

    private static string? ReadInformationalVersion()
        => typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
}
