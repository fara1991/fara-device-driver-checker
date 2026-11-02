namespace FaraDeviceDriverChecker.Services;

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Models;
using Utilities;

/// <summary>残骸チェックの結果一式。</summary>
public class LeftoverScanResult
{
    public DateTime ScannedAt { get; init; } = DateTime.Now;
    public bool IsAdministrator { get; init; }
    public List<LeftoverItem> Items { get; init; } = [];
}

/// <summary>
/// アンインストール漏れ（残骸）を検出する。
/// すべて読み取り専用で、レジストリ・ファイル・サービスの変更は一切行わない。
/// </summary>
public class LeftoverScanner
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string InstallerProductsPath = @"SOFTWARE\Classes\Installer\Products";
    private const string InstallerDependenciesPath = @"SOFTWARE\Classes\Installer\Dependencies";
    private const string ServicesPath = @"SYSTEM\CurrentControlSet\Services";
    private const string UserDataProductsPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData\S-1-5-18\Products";

    // サービスのType（レジストリ値）
    private const int ServiceTypeNone = 0;
    private const int ServiceTypeKernelDriver = 1;
    private const int ServiceTypeFileSystemDriver = 2;

    // サービスのStart（レジストリ値）
    private const int StartBoot = 0;
    private const int StartSystem = 1;
    private const int StartAuto = 2;
    private const int StartDisabled = 4;

    private const string CatDriverRef = "存在しないINFの参照";
    private const string CatService = "サービスの残り";
    private const string CatFolder = "フォルダーの残り";
    private const string CatRegistry = "レジストリの残り";
    private const string CatDuplicate = "旧版ドライバーの重複";
    private const string CatProblem = "問題のあるデバイス";
    private const string CatInfo = "スキャン情報";

    /// <summary>既知の残骸パターン（Intel RSTの事例）。フォルダーが残り、製品の登録が無い場合に報告する。</summary>
    private static readonly (string RelativeFolder, string NameKeyword)[] KnownProductFolders =
    [
        (@"Intel\Intel(R) Rapid Storage Technology", "Rapid Storage")
    ];

    private record UninstallEntry(
        string View, string KeyName, string RegistryPath, string DisplayName,
        string UninstallString, string InstallLocation, string BundleCachePath, bool IsMsi);

    private readonly List<LeftoverItem> _items = [];
    private bool _userDataDenied;
    private int _unreadablePaths;

    public LeftoverScanResult Scan()
    {
        _items.Clear();
        _userDataDenied = false;
        _unreadablePaths = 0;

        var entries = Guarded("Uninstall情報の読み取り", ReadUninstallEntries) ?? [];
        var packages = Guarded("pnputil /enum-drivers", LoadDriverPackages);
        var storeInfNames = BuildStoreInfNames(packages);

        Guarded("Uninstall情報の整合性", () => CheckUninstallEntries(entries));
        Guarded("Installer\\Products / UserData", () => CheckInstallerRegistrations(entries, storeInfNames));
        Guarded("デバイスが参照するINF", CheckDeviceInfReferences);
        Guarded("サービス", CheckServices);
        Guarded("カーネルドライバーのサービス登録", CheckKernelDriverServices);
        Guarded("Package Cache", () => CheckPackageCache(entries));
        Guarded("プログラムフォルダー", () => CheckProgramFolders(entries));
        Guarded("旧版ドライバーの重複", () => CheckDuplicateDrivers(packages));
        Guarded("問題のあるデバイス", CheckProblemDevices);

        if (_unreadablePaths > 0)
        {
            Add(CatInfo, LeftoverSeverity.Info, $"読み取れないフォルダーが{_unreadablePaths}件あります",
                "Program Files / Package Cache",
                "アクセスが拒否されたか、読み取り中にエラーが発生したため、確認できませんでした。");
        }

        if (_userDataDenied)
        {
            Add(CatInfo, LeftoverSeverity.Info, "Installer\\UserDataを読み取れませんでした",
                UserDataProductsPath,
                "アクセスが拒否されました。管理者として起動し直すとMSIのキャッシュ登録も確認できます。");
        }

        var order = new[] { LeftoverSeverity.Review, LeftoverSeverity.Caution, LeftoverSeverity.Info };
        return new LeftoverScanResult
        {
            IsAdministrator = IsAdministrator(),
            Items = _items.OrderBy(i => Array.IndexOf(order, i.Severity)).ThenBy(i => i.Category).ToList()
        };
    }

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    // ---- 共通ヘルパー ----

    private T? Guarded<T>(string name, Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            Add(CatInfo, LeftoverSeverity.Info, $"「{name}」の確認に失敗しました", name, ex.Message);
            return default;
        }
    }

    private void Guarded(string name, Action action) => Guarded<object?>(name, () => { action(); return null; });

    private void Add(string category, LeftoverSeverity severity, string title, string target,
        string detail = "", string command = "", bool isProtected = false)
    {
        _items.Add(new LeftoverItem
        {
            Category = category,
            Severity = severity,
            Title = title,
            Target = target,
            Detail = detail,
            SuggestedCommand = command,
            IsProtected = isProtected
        });
    }

    private static string? ReadString(RegistryKey key, string name)
        => key.GetValue(name) as string;

    private static RegistryKey OpenHklm64()
        => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

    /// <summary>読めなかった場合に、結果から除外して続行してよい種類の例外か。</summary>
    private static bool IsAccessProblem(Exception ex)
        => ex is IOException or UnauthorizedAccessException or SecurityException;

    // ---- 案内コマンドの生成 ----
    // 引用符の外へ抜け出せる値（キー名やフォルダー名に " や ' を含む場合など）は、コマンドを空にして案内しない。

    private static string DeleteWithBackupCommand(string regPath, string backupName)
        => CommandGuard.IsSafeInDoubleQuotes(regPath) && CommandGuard.IsSafeInDoubleQuotes(backupName)
            ? $"reg export \"{regPath}\" \"{backupName}.reg\"\r\nreg delete \"{regPath}\" /f"
            : "";

    private static string JoinCommands(params string[] commands)
        => string.Join("\r\n", commands.Where(c => c != ""));

    private static string ServiceCommand(string name)
        => CommandGuard.IsSafeInDoubleQuotes(name)
            ? $"sc.exe qc \"{name}\"\r\nsc.exe delete \"{name}\""
            : "";

    private static string RemoveFolderCommand(string path, string options)
        => CommandGuard.IsSafeInSingleQuotes(path) ? $"Remove-Item -LiteralPath '{path}' {options}" : "";

    private static string DeleteDriverCommand(string publishedName)
        => Regex.IsMatch(publishedName, @"^oem\d+\.inf$", RegexOptions.IgnoreCase)
            ? $"pnputil /delete-driver {publishedName}"
            : "";

    private static string GetPnpDeviceCommand(string? instanceId)
        => CommandGuard.IsSafeInSingleQuotes(instanceId)
            ? $"Get-PnpDevice -InstanceId '{instanceId}' | Format-List *"
            : "";

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
                    ReadString(key, "DisplayName") ?? "",
                    ReadString(key, "UninstallString") ?? "",
                    ReadString(key, "InstallLocation") ?? "",
                    ReadString(key, "BundleCachePath") ?? "",
                    (key.GetValue("WindowsInstaller") as int? ?? 0) == 1));
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
            var productName = product == null ? "" : ReadString(product, "ProductName") ?? "";
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
                    localPackage = ReadString(props, "LocalPackage");
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
                var name = props == null ? "" : ReadString(props, "DisplayName") ?? "";
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

    // ---- デバイスが参照するINF ----

    private void CheckDeviceInfReferences()
    {
        var infDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF");
        using var searcher = new ManagementObjectSearcher(
            "SELECT DeviceName, DeviceID, InfName FROM Win32_PnPSignedDriver WHERE InfName IS NOT NULL");
        foreach (var o in searcher.Get())
        {
            var inf = o["InfName"]?.ToString();
            if (inf == null || !Regex.IsMatch(inf, @"^oem\d+\.inf$", RegexOptions.IgnoreCase)) continue;
            if (File.Exists(Path.Combine(infDir, inf))) continue;

            var name = o["DeviceName"]?.ToString() ?? "(名前なし)";
            Add(CatDriverRef, LeftoverSeverity.Review,
                $"デバイスが存在しないINFを参照しています: {name}", o["DeviceID"]?.ToString() ?? "",
                $"{inf}が {infDir}にありません。ドライバーの再インストールが必要な可能性があります。",
                GetPnpDeviceCommand(o["DeviceID"]?.ToString()), StorageGuard.IsStorageName(inf));
        }
    }

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
                    $"状態={state} / 開始={startMode} / 実体なし: {exe}", command);
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

        var type = key.GetValue("Type") as int? ?? ServiceTypeNone;
        if (type is not (ServiceTypeKernelDriver or ServiceTypeFileSystemDriver)) return;

        // 無効のものは影響が無いので対象外
        var start = key.GetValue("Start") as int? ?? StartDisabled;
        if (start >= StartDisabled) return;

        var image = PathResolver.ResolveDriverImagePath(ReadString(key, "ImagePath"), name, windir);
        if (File.Exists(image)) return;

        var isStorage = StorageGuard.IsStorageName(name) || StorageGuard.IsStorageName(Path.GetFileName(image));
        var regPath = $@"HKLM\{ServicesPath}\{name}";
        var startText = start switch { StartBoot => "Boot", StartSystem => "System", StartAuto => "Auto", _ => "Manual" };
        var loadedAtBoot = start <= StartAuto;
        Add(CatService, loadedAtBoot ? LeftoverSeverity.Review : LeftoverSeverity.Caution,
            $"実体の無いドライバーのサービス登録: {name}", regPath,
            $"開始={startText} / ドライバーファイルが見つかりません: {image}。" +
            (loadedAtBoot ? "起動時に読み込まれる設定のため、起動エラーの原因になり得ます。" : "") +
            (isStorage ? "ストレージ系のため、手動で削除しないでください。" : ""),
            isStorage ? "" : DeleteWithBackupCommand(regPath, $"backup-service-{name}"), isStorage);
    }

    // ---- フォルダー ----

    private void CheckPackageCache(List<UninstallEntry> entries)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Package Cache");
        if (!Directory.Exists(root)) return;

        using var hklm = OpenHklm64();
        var registered = entries.Select(e => e.KeyName.ToUpperInvariant()).ToHashSet();

        foreach (var dir in Directory.GetDirectories(root))
        {
            var m = Regex.Match(Path.GetFileName(dir), @"^(\{[0-9A-Fa-f-]{36}\})");
            if (!m.Success) continue;

            var guid = m.Groups[1].Value.ToUpperInvariant();
            if (registered.Contains(guid)) continue;
            if (entries.Any(e => e.BundleCachePath.Contains(guid, StringComparison.OrdinalIgnoreCase) ||
                                 e.UninstallString.Contains(guid, StringComparison.OrdinalIgnoreCase))) continue;

            var packed = MsiGuid.Pack(guid);
            using var prod = packed == null ? null : hklm.OpenSubKey($@"{InstallerProductsPath}\{packed}");
            using var dep = hklm.OpenSubKey($@"{InstallerDependenciesPath}\{guid}");
            if (prod != null || dep != null) continue;

            Add(CatFolder, LeftoverSeverity.Caution,
                $"登録の無いPackage Cache: {Path.GetFileName(dir)}", dir,
                $"Uninstall・Installer\\Products・Dependenciesのいずれにも {guid}が見つかりません。" +
                $"サイズ: {FormatSize(DirectorySize(dir))}",
                RemoveFolderCommand(dir, "-Recurse -Force"));
        }
    }

    private void CheckProgramFolders(List<UninstallEntry> entries)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(r => !string.IsNullOrEmpty(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        using var hklm = OpenHklm64();
        var productNames = new List<string>(entries.Select(e => e.DisplayName));
        using (var products = hklm.OpenSubKey(InstallerProductsPath))
        {
            foreach (var packed in products?.GetSubKeyNames() ?? [])
            {
                using var p = products!.OpenSubKey(packed);
                if (p != null) productNames.Add(ReadString(p, "ProductName") ?? "");
            }
        }

        foreach (var root in roots)
        {
            foreach (var (relative, keyword) in KnownProductFolders)
            {
                var folder = Path.Combine(root, relative);
                if (!Directory.Exists(folder)) continue;
                if (productNames.Any(n => n.Contains(keyword, StringComparison.OrdinalIgnoreCase))) continue;

                Add(CatFolder, LeftoverSeverity.Review,
                    $"登録の無い製品フォルダー: {relative}", folder,
                    $"「{keyword}」を含む製品がUninstall / Installer\\Productsに登録されていません。" +
                    "アンインストール後に残ったフォルダーの可能性があります。",
                    RemoveFolderCommand(folder, "-Recurse -Force"));
            }

            // 中身がファイルを1つも持たない空のフォルダー（ベンダー直下）
            foreach (var vendor in SafeDirectories(root))
            {
                if (SafeHasAnyFile(vendor)) continue;
                Add(CatFolder, LeftoverSeverity.Info,
                    $"ファイルの無い空のフォルダー: {Path.GetFileName(vendor)}", vendor,
                    "配下にファイルが1つもありません。",
                    RemoveFolderCommand(vendor, "-Recurse"));
            }
        }
    }

    private IEnumerable<string> SafeDirectories(string path)
    {
        try
        {
            return Directory.GetDirectories(path);
        }
        catch (Exception ex) when (IsAccessProblem(ex))
        {
            _unreadablePaths++;
            return [];
        }
    }

    private bool SafeHasAnyFile(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Any();
        }
        catch (Exception ex) when (IsAccessProblem(ex))
        {
            _unreadablePaths++;
            return true; // 読めないフォルダーは空と判断しない
        }
    }

    private long DirectorySize(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        }
        catch (Exception ex) when (IsAccessProblem(ex))
        {
            _unreadablePaths++;
            return 0;
        }
    }

    private static string FormatSize(long bytes)
        => bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0:F1} MB" : $"{bytes / 1024.0:F1} KB";

    // ---- ドライバーストア ----

    private static List<DriverPackage> LoadDriverPackages()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var psi = new ProcessStartInfo
        {
            // 同名の実行ファイルを置かれても読み込まないよう、System32 の絶対パスを指定する
            FileName = Path.Combine(Environment.SystemDirectory, "pnputil.exe"),
            Arguments = "/enum-drivers",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage)
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("pnputilを起動できませんでした");
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return PnpUtilParser.Parse(output);
    }

    /// <summary>ドライバーストアにあるINFの元の名前（小文字）の集合。pnputilとFileRepositoryの和集合。</summary>
    private static HashSet<string>? BuildStoreInfNames(List<DriverPackage>? packages)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in packages ?? []) names.Add(p.OriginalName);

        var repo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            @"System32\DriverStore\FileRepository");
        try
        {
            foreach (var dir in Directory.GetDirectories(repo))
            {
                var m = Regex.Match(Path.GetFileName(dir), @"^(.+?\.inf)_", RegexOptions.IgnoreCase);
                if (m.Success) names.Add(m.Groups[1].Value);
            }
        }
        catch (Exception ex) when (IsAccessProblem(ex))
        {
            if (packages == null) return null;
        }
        return names.Count == 0 ? null : names;
    }

    private void CheckDuplicateDrivers(List<DriverPackage>? packages)
    {
        if (packages == null) return;

        var inUse = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var searcher = new ManagementObjectSearcher("SELECT InfName FROM Win32_PnPSignedDriver WHERE InfName IS NOT NULL"))
        {
            foreach (var o in searcher.Get())
            {
                if (o["InfName"]?.ToString() is { } inf) inUse.Add(inf);
            }
        }

        var groups = packages
            .Where(p => p.OriginalName != "")
            .GroupBy(p => p.OriginalName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var group in groups)
        {
            var ordered = group.OrderByDescending(p => p.Version).ThenByDescending(p => p.Date).ToList();
            var newest = ordered[0];

            foreach (var old in ordered.Skip(1))
            {
                var used = inUse.Contains(old.PublishedName);
                var isStorage = StorageGuard.IsStorageName(old.OriginalName) ||
                                StorageGuard.IsStorageClass(old.ClassGuid) ||
                                StorageGuard.IsStorageName(old.ClassName);
                var detail = $"最新: {newest.PublishedName} (v{newest.Version}) / この旧版: {old.PublishedName} (v{old.Version}, {old.Date:yyyy/MM/dd}) / " +
                             $"プロバイダー: {old.Provider} / クラス: {old.ClassName}。" +
                             (used ? "デバイスが使用中のため削除しないでください。" : "どのデバイスにも使われていません。") +
                             (isStorage ? "ストレージ系のため削除対象として提案しません。" : "");
                Add(CatDuplicate, used || isStorage ? LeftoverSeverity.Info : LeftoverSeverity.Caution,
                    $"同名ドライバーの旧版: {old.OriginalName} ({old.PublishedName})", old.PublishedName, detail,
                    used || isStorage ? "" : DeleteDriverCommand(old.PublishedName), isStorage);
            }
        }
    }

    // ---- 問題のあるデバイス ----

    private void CheckProblemDevices()
    {
        // Get-PnpDevice -PresentOnlyでStatusがOK以外のデバイス相当
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, DeviceID, Status, ConfigManagerErrorCode, PNPClass FROM Win32_PnPEntity " +
            "WHERE ConfigManagerErrorCode <> 0 OR (Status IS NOT NULL AND Status <> 'OK')");
        foreach (var o in searcher.Get())
        {
            var code = o["ConfigManagerErrorCode"] is uint c ? c : 0;
            var status = o["Status"]?.ToString() ?? "不明";
            var name = o["Name"]?.ToString() ?? "(名前なし)";
            var id = o["DeviceID"]?.ToString() ?? "";
            Add(CatProblem, code != 0 ? LeftoverSeverity.Review : LeftoverSeverity.Caution,
                $"{name}", id,
                $"Status={status} / クラス={o["PNPClass"]} / エラーコード={code}（{ErrorCodeHelper.GetProblemDescription(code)}）",
                GetPnpDeviceCommand(id));
        }
    }
}
