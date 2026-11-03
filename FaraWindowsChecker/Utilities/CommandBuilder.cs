namespace FaraWindowsChecker.Utilities;

using System.Text.RegularExpressions;

/// <summary>
/// 残骸チェックの「案内コマンド」を組み立てる。
/// 引用符の外へ抜け出せる値（キー名やフォルダー名に " や ' を含む場合など）は、コマンドを空にして案内しない。
/// </summary>
public static class CommandBuilder
{
    private static readonly Regex PublishedNamePattern = new(@"^oem\d+\.inf\z", RegexOptions.IgnoreCase);

    /// <summary>レジストリキーをreg exportでバックアップしてから削除するコマンド。</summary>
    public static string DeleteWithBackupCommand(string regPath, string backupName)
        => CommandGuard.IsSafeInDoubleQuotes(regPath) && CommandGuard.IsSafeInDoubleQuotes(backupName)
            ? $"reg export \"{regPath}\" \"{backupName}.reg\"\r\nreg delete \"{regPath}\" /f"
            : "";

    /// <summary>空のコマンドを除いて、改行で連結する。</summary>
    public static string JoinCommands(params string[] commands)
        => string.Join("\r\n", commands.Where(c => c != ""));

    public static string ServiceCommand(string name)
        => CommandGuard.IsSafeInDoubleQuotes(name)
            ? $"sc.exe qc \"{name}\"\r\nsc.exe delete \"{name}\""
            : "";

    public static string RemoveFolderCommand(string path, string options)
        => CommandGuard.IsSafeInSingleQuotes(path) ? $"Remove-Item -LiteralPath '{path}' {options}" : "";

    /// <summary>公開名（oemNN.inf）の形のときだけ、pnputil /delete-driverのコマンドを返す。</summary>
    public static string DeleteDriverCommand(string publishedName)
        => PublishedNamePattern.IsMatch(publishedName)
            ? $"pnputil /delete-driver {publishedName}"
            : "";

    public static string GetPnpDeviceCommand(string? instanceId)
        => CommandGuard.IsSafeInSingleQuotes(instanceId)
            ? $"Get-PnpDevice -InstanceId '{instanceId}' | Format-List *"
            : "";
}
