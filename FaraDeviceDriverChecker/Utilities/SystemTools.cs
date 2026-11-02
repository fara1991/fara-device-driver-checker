namespace FaraDeviceDriverChecker.Utilities;

using System.IO;

/// <summary>
/// 外部の実行ファイルの場所。名前だけで起動すると、アプリのフォルダーや現在のフォルダーに置かれた
/// 同名のファイルが先に起動されるおそれがあるため、System32 の絶対パスを使う。
/// </summary>
public static class SystemTools
{
    public static string PnpUtil => Path.Combine(Environment.SystemDirectory, "pnputil.exe");

    public static string PowerShell => Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
}
