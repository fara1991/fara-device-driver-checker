namespace FaraDeviceDriverChecker.Services;

using System.Globalization;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Models;

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
public partial class LeftoverScanner
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string InstallerProductsPath = @"SOFTWARE\Classes\Installer\Products";
    private const string InstallerDependenciesPath = @"SOFTWARE\Classes\Installer\Dependencies";
    private const string ServicesPath = @"SYSTEM\CurrentControlSet\Services";
    private const string UserDataProductsPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData\S-1-5-18\Products";

    /// <summary>読み取るレジストリの値名。</summary>
    private static class RegValue
    {
        public const string DisplayName = "DisplayName";
        public const string UninstallString = "UninstallString";
        public const string InstallLocation = "InstallLocation";
        public const string BundleCachePath = "BundleCachePath";
        public const string WindowsInstaller = "WindowsInstaller";
        public const string ProductName = "ProductName";
        public const string LocalPackage = "LocalPackage";
        public const string Type = "Type";
        public const string Start = "Start";
        public const string ImagePath = "ImagePath";
    }

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
        var signedDrivers = Guarded("署名済みドライバーの情報", LoadSignedDrivers);
        Guarded("デバイスが参照するINF", () => CheckDeviceInfReferences(signedDrivers));
        Guarded("サービス", CheckServices);
        Guarded("カーネルドライバーのサービス登録", CheckKernelDriverServices);
        Guarded("Package Cache", () => CheckPackageCache(entries));
        Guarded("プログラムフォルダー", () => CheckProgramFolders(entries));
        Guarded("旧版ドライバーの重複", () => CheckDuplicateDrivers(packages, signedDrivers));
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
}
