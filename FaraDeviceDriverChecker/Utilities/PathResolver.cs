namespace FaraDeviceDriverChecker.Utilities;

using System.IO;
using System.Text.RegularExpressions;

/// <summary>コマンドライン文字列やサービスのImagePathから、実在確認できるパスを取り出す。</summary>
public static class PathResolver
{
    /// <summary>
    /// コマンドライン（UninstallStringなど）から実行ファイルのパスを取り出す。
    /// 環境変数は展開する。取り出せない場合はnull。
    /// </summary>
    public static string? ExtractExePath(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return null;
        var text = Environment.ExpandEnvironmentVariables(commandLine.Trim());
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : null;
        }
        var m = Regex.Match(text, @"^(.+?\.(exe|bat|cmd|com))(\s|$)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>
    /// カーネルドライバーのサービス登録（ImagePath）を絶対パスに解決する。
    /// ImagePathが無い場合は既定のSystem32\drivers\&lt;サービス名&gt;.sysとみなす。
    /// </summary>
    /// <param name="imagePath">レジストリのImagePathの値。無ければnullまたは空。</param>
    /// <param name="serviceName">サービスのキー名。</param>
    /// <param name="windowsDir">Windowsフォルダー（例: C:\Windows）。</param>
    public static string ResolveDriverImagePath(string? imagePath, string serviceName, string windowsDir)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return Path.Combine(windowsDir, "System32", "drivers", serviceName + ".sys");

        var text = Environment.ExpandEnvironmentVariables(imagePath.Trim().Trim('"'));

        if (text.StartsWith(@"\??\", StringComparison.Ordinal))
            return text[4..];

        const string systemRoot = @"\SystemRoot\";
        if (text.StartsWith(systemRoot, StringComparison.OrdinalIgnoreCase))
            return Path.Combine(windowsDir, text[systemRoot.Length..]);

        if (Path.IsPathRooted(text) && text.Length > 1 && text[1] == ':')
            return text;

        // "System32\DRIVERS\xxx.sys" のようにWindowsフォルダー相対で書かれた値
        return Path.Combine(windowsDir, text.TrimStart('\\'));
    }
}
