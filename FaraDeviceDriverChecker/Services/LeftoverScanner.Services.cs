namespace FaraDeviceDriverChecker.Services;

using System.Globalization;
using System.IO;
using System.Management;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Models;
using Utilities;
using static FaraDeviceDriverChecker.Utilities.CommandBuilder;

// 残骸チェック: サービス（ユーザーモードのサービスと、カーネルドライバーのサービス登録）
public partial class LeftoverScanner
{
    // ---- サービス ----

    private void CheckServices()
    {
        var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };

        // Win32_Serviceはユーザーモードのサービスのみ（カーネルドライバーは含まない）
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, DisplayName, State, StartMode, PathName FROM Win32_Service");
        foreach (var o in searcher.Get())
        {
            var name = o["Name"]?.ToString() ?? "";
            var display = o["DisplayName"]?.ToString() ?? name;
            var state = o["State"]?.ToString() ?? "";
            var startMode = o["StartMode"]?.ToString() ?? "";
            var exe = PathResolver.ExtractExePath(o["PathName"]?.ToString() ?? "");
            if (name == "" || exe == null || !Path.IsPathRooted(exe)) continue;

            // Windows本体のサービスは対象外にする
            if (exe.StartsWith(windir, StringComparison.OrdinalIgnoreCase)) continue;

            var command = ServiceCommand(name);
            if (!File.Exists(exe))
            {
                Add(CatService, LeftoverSeverity.Review,
                    $"実行ファイルの無いサービス: {display}", name,
                    $"状態: {state} / 開始: {startMode} / 実体なし: {exe}", command);
            }
            else if (state == "Stopped" && startMode == "Disabled" &&
                     programRoots.Any(r => exe.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
            {
                var known = name.StartsWith("IAStor", StringComparison.OrdinalIgnoreCase)
                    ? "Intel RSTのサービスで、アンインストール後に残る例があります。" : "";
                Add(CatService, LeftoverSeverity.Caution,
                    $"停止・無効のまま残るサービス: {display}", name,
                    $"実行ファイル: {exe}。{known}利用者が意図して無効化している場合は対象外です。", command);
            }
        }
    }

    // ---- カーネルドライバーのサービス登録 ----

    /// <summary>実体の.sysが無いのに残っているカーネルドライバーのサービス登録を検出する。</summary>
    private void CheckKernelDriverServices()
    {
        var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        using var hklm = OpenHklm64();
        using var services = hklm.OpenSubKey(ServicesPath);
        if (services == null) return;

        // 1件の読み取り失敗でチェック全体を止めず、読めなかった件数を情報として報告する
        var unreadable = 0;
        foreach (var name in services.GetSubKeyNames())
        {
            try
            {
                CheckKernelDriverService(services, name, windir);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
            {
                unreadable++;
            }
        }

        if (unreadable > 0)
        {
            Add(CatInfo, LeftoverSeverity.Info, $"読み取れないサービス登録が{unreadable}件あります",
                $@"HKLM\{ServicesPath}",
                "アクセスが拒否されたため確認できませんでした。管理者として起動し直すと確認できる場合があります。");
        }
    }

    private void CheckKernelDriverService(RegistryKey services, string name, string windir)
    {
        using var key = services.OpenSubKey(name);
        if (key == null) return;

        var type = key.GetValue(RegValue.Type) as int? ?? ServiceTypeNone;
        if (type is not (ServiceTypeKernelDriver or ServiceTypeFileSystemDriver)) return;

        // 無効のものは影響が無いので対象外
        var start = key.GetValue(RegValue.Start) as int? ?? StartDisabled;
        if (start >= StartDisabled) return;

        var image = PathResolver.ResolveDriverImagePath(ReadString(key, RegValue.ImagePath), name, windir);
        if (File.Exists(image)) return;

        var isStorage = StorageGuard.IsStorageName(name) || StorageGuard.IsStorageName(Path.GetFileName(image));
        var regPath = $@"HKLM\{ServicesPath}\{name}";
        var startText = start switch { StartBoot => "Boot", StartSystem => "System", StartAuto => "Auto", _ => "Manual" };
        var loadedAtBoot = start <= StartAuto;
        Add(CatService, loadedAtBoot ? LeftoverSeverity.Review : LeftoverSeverity.Caution,
            $"実体の無いドライバーのサービス登録: {name}", regPath,
            $"開始: {startText} / ドライバーファイルが見つかりません: {image}。" +
            (loadedAtBoot ? "起動時に読み込まれる設定のため、起動エラーの原因になり得ます。" : "") +
            (isStorage ? "ストレージ系のため、手動で削除しないでください。" : ""),
            isStorage ? "" : DeleteWithBackupCommand(regPath, $"backup-service-{name}"), isStorage);
    }
}
