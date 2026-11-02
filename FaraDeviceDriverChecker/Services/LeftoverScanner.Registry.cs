namespace FaraDeviceDriverChecker.Services;

using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Models;
using Utilities;
using static FaraDeviceDriverChecker.Utilities.CommandBuilder;

// 残骸チェック: レジストリ（Uninstall・InstallerProducts・UserData）
public partial class LeftoverScanner
{
    // ---- レジストリ: Uninstall ----

    private List<UninstallEntry> ReadUninstallEntries()
    {
        var list = new List<UninstallEntry>();
        var sources = new[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM (通常)", @"HKLM\" + UninstallPath),
            (RegistryHive.LocalMachine, RegistryView.Registry32, "HKLM (WOW6432Node)", @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (RegistryHive.CurrentUser, RegistryView.Registry64, "HKCU", @"HKCU\" + UninstallPath)
        };

        foreach (var (hive, view, label, display) in sources)
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var root = baseKey.OpenSubKey(UninstallPath);
            if (root == null) continue;

            foreach (var name in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(name);
                if (key == null) continue;
                list.Add(new UninstallEntry(
                    label, name, $@"{display}\{name}",
                    ReadString(key, RegValue.DisplayName) ?? "",
                    ReadString(key, RegValue.UninstallString) ?? "",
                    ReadString(key, RegValue.InstallLocation) ?? "",
                    ReadString(key, RegValue.BundleCachePath) ?? "",
                    (key.GetValue(RegValue.WindowsInstaller) as int? ?? 0) == 1));
            }
        }
        return list;
    }

    private void CheckUninstallEntries(List<UninstallEntry> entries)
    {
        foreach (var e in entries.Where(e => e.DisplayName != ""))
        {
            var command = DeleteWithBackupCommand(e.RegistryPath, $"backup-uninstall-{e.KeyName.Trim('{', '}')}");
            var exe = PathResolver.ExtractExePath(e.UninstallString);

            if (exe != null && Path.IsPathRooted(exe) && !File.Exists(exe))
            {
                Add(CatRegistry, LeftoverSeverity.Review,
                    $"アンインストーラーが存在しません: {e.DisplayName}", e.RegistryPath,
                    $"{e.View}のUninstall情報が、存在しない実行ファイルを指しています: {exe}", command);
                continue;
            }

            if (e.InstallLocation != "" && Path.IsPathRooted(e.InstallLocation) && !Directory.Exists(e.InstallLocation))
            {
                Add(CatRegistry, LeftoverSeverity.Caution,
                    $"インストール先が存在しません: {e.DisplayName}", e.RegistryPath,
                    $"{e.View}のInstallLocationが見つかりません: {e.InstallLocation}", command);
                continue;
            }

            // MSI製品なのにInstaller\Productsへの登録が無い
            var isMsiKey = MsiGuid.IsGuid(e.KeyName) && e.View != "HKCU" &&
                           (e.IsMsi || e.UninstallString.Contains("msiexec", StringComparison.OrdinalIgnoreCase));
            if (isMsiKey && MsiGuid.Pack(e.KeyName) is { } packed && !ProductRegistered(packed))
            {
                Add(CatRegistry, LeftoverSeverity.Review,
                    $"MSIの登録が無いUninstall情報: {e.DisplayName}", e.RegistryPath,
                    $"{e.View}にUninstall情報がありますが、HKLM\\{InstallerProductsPath}\\{packed}がありません。" +
                    "アンインストールが失敗する原因になります。", command);
            }
        }
    }

    private static bool ProductRegistered(string packedGuid)
    {
        using var hklm = OpenHklm64();
        using var key = hklm.OpenSubKey($@"{InstallerProductsPath}\{packedGuid}");
        return key != null;
    }

    // ---- レジストリ: Installer\Products / UserData、MSIが参照するINF ----

    private void CheckInstallerRegistrations(List<UninstallEntry> entries, HashSet<string>? storeInfNames)
    {
        using var hklm = OpenHklm64();
        var uninstallGuids = entries.Where(e => e.View != "HKCU")
            .Select(e => e.KeyName.ToUpperInvariant()).ToHashSet();

        using var products = hklm.OpenSubKey(InstallerProductsPath);
        if (products == null) return;

        CheckUserDataOrphans(hklm, products.GetSubKeyNames().ToHashSet(StringComparer.OrdinalIgnoreCase));

        foreach (var packed in products.GetSubKeyNames())
        {
            var guid = MsiGuid.Unpack(packed);
            if (guid == null) continue;

            using var product = products.OpenSubKey(packed);
            var productName = product == null ? "" : ReadString(product, RegValue.ProductName) ?? "";
            var label = productName != "" ? productName : guid;
            var productsPath = $@"HKLM\{InstallerProductsPath}\{packed}";

            string? localPackage = null;
            var userDataKnown = false;
            try
            {
                using var props = hklm.OpenSubKey($@"{UserDataProductsPath}\{packed}\InstallProperties");
                if (props != null)
                {
                    userDataKnown = true;
                    localPackage = ReadString(props, RegValue.LocalPackage);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
            {
                _userDataDenied = true;
            }

            var userDataPath = $@"HKLM\{UserDataProductsPath}\{packed}";

            if (userDataKnown && !string.IsNullOrEmpty(localPackage) && !File.Exists(localPackage))
            {
                Add(CatRegistry, LeftoverSeverity.Review,
                    $"キャッシュ済みMSIが存在しません: {label}", userDataPath,
                    $"LocalPackageが見つかりません: {localPackage}。修復・アンインストールが失敗する原因になります。",
                    JoinCommands(DeleteWithBackupCommand(userDataPath, $"backup-userdata-{packed}"),
                        DeleteWithBackupCommand(productsPath, $"backup-products-{packed}")));
            }
            else if (!uninstallGuids.Contains(guid) && !_userDataDenied && !userDataKnown)
            {
                Add(CatRegistry, LeftoverSeverity.Caution,
                    $"Installer\\Productsにだけ残る登録: {label}", productsPath,
                    "Uninstall情報もUserDataのInstallPropertiesもありません。",
                    DeleteWithBackupCommand(productsPath, $"backup-products-{packed}"));
            }

            if (storeInfNames != null && !string.IsNullOrEmpty(localPackage) && File.Exists(localPackage))
            {
                CheckMsiInfReferences(label, guid, localPackage, storeInfNames);
            }
        }
    }

    /// <summary>Installer\Productsを消した後にInstaller\UserDataだけが残った登録を検出する。</summary>
    private void CheckUserDataOrphans(RegistryKey hklm, HashSet<string> productPacked)
    {
        try
        {
            using var userData = hklm.OpenSubKey(UserDataProductsPath);
            if (userData == null) return;

            foreach (var packed in userData.GetSubKeyNames())
            {
                if (productPacked.Contains(packed) || MsiGuid.Unpack(packed) is not { } guid) continue;

                using var props = userData.OpenSubKey($@"{packed}\InstallProperties");
                var name = props == null ? "" : ReadString(props, RegValue.DisplayName) ?? "";
                var userDataPath = $@"HKLM\{UserDataProductsPath}\{packed}";
                Add(CatRegistry, LeftoverSeverity.Caution,
                    $"Installer\\UserDataにだけ残る登録: {(name != "" ? name : guid)}", userDataPath,
                    $"Installer\\Products\\{packed}が無いのにUserDataが残っています（製品コード {guid}）。" +
                    "Productsだけを手動で削除した後などに起きます。",
                    DeleteWithBackupCommand(userDataPath, $"backup-userdata-{packed}"));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            _userDataDenied = true;
        }
    }

    /// <summary>MSIが同梱するINFが、ドライバーストアに存在するか確認する。</summary>
    private void CheckMsiInfReferences(string label, string productCode, string localPackage, HashSet<string> storeInfNames)
    {
        using var db = MsiDatabase.TryOpen(localPackage);
        if (db == null) return;

        var infNames = db.QueryFirstColumn("SELECT `FileName` FROM `File`")
            .Select(f => f.Contains('|') ? f[(f.IndexOf('|') + 1)..] : f)
            .Where(f => f.EndsWith(".inf", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        // DIFxのドライバー登録テーブルがあるMSIだけを、非ストレージ系の検出対象にする
        // （SDKのようにINFを単なるファイルとして同梱するだけのMSIの誤検出を避ける）
        var hasDriverTable = db.QueryFirstColumn("SELECT `Component` FROM `MsiDriverPackages`").Count > 0;

        foreach (var inf in infNames)
        {
            if (storeInfNames.Contains(inf)) continue;

            var isStorage = StorageGuard.IsStorageName(inf);
            if (!isStorage && !hasDriverTable) continue;
            Add(CatDriverRef, isStorage ? LeftoverSeverity.Review : LeftoverSeverity.Caution,
                $"{label}が参照するINFがドライバーストアにありません: {inf}", productCode,
                "アンインストール時にドライバー削除（DiUninstallDriver）が0x490 (ERROR_NOT_FOUND) で失敗し、" +
                "MSIが1603で終了する原因になります。" +
                (isStorage ? "ストレージ系のINFのため、手動で削除・登録解除しないでください。" : "") +
                $"（参照元MSI: {localPackage}。Fileテーブルの.infを基にした推定で、未選択の機能のINFも含み得ます）",
                "pnputil /enum-drivers", isStorage);
        }
    }
}
