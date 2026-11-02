namespace FaraDeviceDriverChecker.Services;

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using Models;
using Utilities;
using static FaraDeviceDriverChecker.Utilities.CommandBuilder;

// 残骸チェック: ドライバー（INFの参照・ドライバーストア・旧版の重複・問題のあるデバイス）
public partial class LeftoverScanner
{
    // ---- デバイスが参照するINF ----

    /// <summary>デバイスに割り当てられた署名済みドライバー（WMIのWin32_PnPSignedDriver）。</summary>
    private record SignedDriver(string DeviceName, string DeviceId, string InfName);

    private static List<SignedDriver> LoadSignedDrivers()
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT DeviceName, DeviceID, InfName FROM Win32_PnPSignedDriver WHERE InfName IS NOT NULL");
        return searcher.Get().Cast<ManagementBaseObject>()
            .Select(o => new SignedDriver(
                o["DeviceName"]?.ToString() ?? "", o["DeviceID"]?.ToString() ?? "", o["InfName"]?.ToString() ?? ""))
            .Where(d => d.InfName != "")
            .ToList();
    }

    private void CheckDeviceInfReferences(List<SignedDriver>? drivers)
    {
        if (drivers == null) return;

        var infDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF");
        foreach (var d in drivers)
        {
            if (!Regex.IsMatch(d.InfName, @"^oem\d+\.inf$", RegexOptions.IgnoreCase)) continue;
            if (File.Exists(Path.Combine(infDir, d.InfName))) continue;

            var name = d.DeviceName != "" ? d.DeviceName : "（名前なし）";
            Add(CatDriverRef, LeftoverSeverity.Review,
                $"デバイスが存在しないINFを参照しています: {name}", d.DeviceId,
                $"{d.InfName}が{infDir}にありません。ドライバーの再インストールが必要な可能性があります。",
                GetPnpDeviceCommand(d.DeviceId), StorageGuard.IsStorageName(d.InfName));
        }
    }

    // ---- ドライバーストア ----

    private static List<DriverPackage> LoadDriverPackages()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var psi = new ProcessStartInfo
        {
            // 同名の実行ファイルを置かれても読み込まないよう、System32の絶対パスを指定する
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

    /// <summary>ドライバーストアにあるINFの元の名前の集合（大文字小文字は区別しない）。pnputilとFileRepositoryの和集合。</summary>
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

    private void CheckDuplicateDrivers(List<DriverPackage>? packages, List<SignedDriver>? drivers)
    {
        if (packages == null || drivers == null) return;

        var inUse = drivers.Select(d => d.InfName).ToHashSet(StringComparer.OrdinalIgnoreCase);

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
                var detail = $"最新: {DescribePackage(newest, includeDate: false)} / この旧版: {DescribePackage(old, includeDate: true)} / " +
                             $"プロバイダー: {old.Provider} / クラス: {old.ClassName}。" +
                             (used ? "デバイスが使用中のため削除しないでください。" : "どのデバイスにも使われていません。") +
                             (isStorage ? "ストレージ系のため削除対象として提案しません。" : "");
                Add(CatDuplicate, used || isStorage ? LeftoverSeverity.Info : LeftoverSeverity.Caution,
                    $"同名ドライバーの旧版: {old.OriginalName} ({old.PublishedName})", old.PublishedName, detail,
                    used || isStorage ? "" : DeleteDriverCommand(old.PublishedName), isStorage);
            }
        }
    }

    /// <summary>「oem12.inf (v1.0.0.0, 2020/01/01)」の形の説明。バージョンや日付が分からないときは「不明」と表示する。</summary>
    private static string DescribePackage(DriverPackage package, bool includeDate)
    {
        var version = package.Version is { } v ? $"v{v}" : "バージョン不明";
        var date = package.Date is { } d ? d.ToString("yyyy/MM/dd") : "日付不明";
        return includeDate
            ? $"{package.PublishedName} ({version}, {date})"
            : $"{package.PublishedName} ({version})";
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
            var name = o["Name"]?.ToString() ?? "（名前なし）";
            var id = o["DeviceID"]?.ToString() ?? "";
            Add(CatProblem, code != 0 ? LeftoverSeverity.Review : LeftoverSeverity.Caution,
                name, id,
                $"Status: {status} / クラス: {o["PNPClass"]} / エラーコード: {code}（{ErrorCodeHelper.GetProblemDescription(code)}）",
                GetPnpDeviceCommand(id));
        }
    }
}
