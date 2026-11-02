namespace FaraDeviceDriverChecker.Services;

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Security;
using Microsoft.Win32;
using Models;
using Utilities;

/// <summary>デバイスのスキャン結果。FailedCount は、デバイスの一覧を取得できなかったクラスの数。</summary>
public record DeviceScanResult(List<DeviceInfo> Devices, int FailedCount);

public class DeviceService
{
    private const string Unknown = "不明";
    private const int ServiceTypeKernelDriver = 1;
    private const int ServiceTypeFileSystemDriver = 2;

    /// <summary>WMI の Win32_PnPEntity から取り出した、デバイス1台分の情報。</summary>
    private record PnpEntity(
        string Name, string DeviceId, string Description, string Manufacturer,
        string Status, string Class, string Service, string HardwareId, uint? ProblemCode);

    /// <summary>WMI の Win32_PnPSignedDriver から取り出した、ドライバーのバージョンと日付（日付は WMI の形式のまま）。</summary>
    private record SignedDriver(string Version, string? RawDate);

    public DeviceScanResult GetDevices(string[] deviceClasses)
    {
        var (deviceIds, failedClasses) = GetDeviceIdsByClasses(deviceClasses);
        if (deviceIds.Count == 0) return new DeviceScanResult([], failedClasses);

        // WMI は、1回の問い合わせに約1秒かかる（1台だけを指定しても、全件を取得しても同じ）。
        // デバイスごとに問い合わせると、数十台で1分以上かかるため、全件を1回で取得してデバイスIDで突き合わせる。
        var entities = LoadPnpEntities();
        var drivers = LoadSignedDrivers();

        var devices = new List<DeviceInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in deviceIds)
        {
            if (!entities.TryGetValue(id, out var entity) || !seen.Add(entity.DeviceId)) continue;
            devices.Add(CreateDeviceInfo(entity, drivers));
        }
        return new DeviceScanResult(devices, failedClasses);
    }

    private static (List<string> Ids, int FailedClasses) GetDeviceIdsByClasses(string[] deviceClasses)
    {
        var deviceIds = new List<string>();
        var failedClasses = 0;

        foreach (var deviceClass in deviceClasses)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = SystemTools.PnpUtil,
                    Arguments = $"/enum-devices /class {deviceClass}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process == null)
                {
                    failedClasses++;
                    continue;
                }

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                deviceIds.AddRange(ParseInstanceIds(output));
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
            {
                failedClasses++;
            }
        }

        return (deviceIds, failedClasses);
    }

    /// <summary>pnputil /enum-devices の出力から、インスタンス ID を取り出す（日本語・英語の表示に対応）。</summary>
    public static List<string> ParseInstanceIds(string output)
    {
        var ids = new List<string>();
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.Contains("インスタンス ID:") && !line.Contains("Instance ID:")) continue;

            var id = line.Split(':').LastOrDefault()?.Trim();
            if (!string.IsNullOrEmpty(id)) ids.Add(id);
        }
        return ids;
    }

    private static Dictionary<string, PnpEntity> LoadPnpEntities()
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, DeviceID, Description, Manufacturer, Status, PNPClass, Service, HardwareID, ConfigManagerErrorCode " +
            "FROM Win32_PnPEntity");

        var entities = new Dictionary<string, PnpEntity>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in searcher.Get().Cast<ManagementBaseObject>())
        {
            using (o)
            {
                var entity = new PnpEntity(
                    WmiHelper.GetPropertyValue(o, "Name"), WmiHelper.GetPropertyValue(o, "DeviceID"),
                    WmiHelper.GetPropertyValue(o, "Description"), WmiHelper.GetPropertyValue(o, "Manufacturer"),
                    WmiHelper.GetPropertyValue(o, "Status"), WmiHelper.GetPropertyValue(o, "PNPClass"),
                    WmiHelper.GetPropertyValue(o, "Service"), WmiHelper.GetPropertyValue(o, "HardwareID"),
                    o["ConfigManagerErrorCode"] is uint code ? code : null);
                entities.TryAdd(entity.DeviceId, entity);
            }
        }
        return entities;
    }

    private static Dictionary<string, SignedDriver> LoadSignedDrivers()
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT DeviceID, DriverVersion, DriverDate FROM Win32_PnPSignedDriver");

        var drivers = new Dictionary<string, SignedDriver>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in searcher.Get().Cast<ManagementBaseObject>())
        {
            using (o)
            {
                var id = o["DeviceID"]?.ToString();
                if (string.IsNullOrEmpty(id)) continue;
                drivers.TryAdd(id, new SignedDriver(o["DriverVersion"]?.ToString() ?? Unknown, o["DriverDate"]?.ToString()));
            }
        }
        return drivers;
    }

    private static DeviceInfo CreateDeviceInfo(PnpEntity entity, Dictionary<string, SignedDriver> drivers)
    {
        var device = new DeviceInfo
        {
            Name = entity.Name,
            DeviceId = entity.DeviceId,
            Description = entity.Description,
            Manufacturer = entity.Manufacturer,
            Status = entity.Status,
            Class = entity.Class,
            Service = entity.Service,
            HardwareId = entity.HardwareId
        };

        if (entity.ProblemCode is { } code)
        {
            device.HasProblem = code != 0;
            device.ProblemCode = ErrorCodeHelper.GetProblemDescription(code);
        }

        try
        {
            ApplyDriverInfo(device, drivers);
        }
        catch (Exception ex)
        {
            device.DriverVersion = $"取得に失敗しました（詳細: {ex.Message}）";
            device.HasProblem = true;
            device.ProblemCode = "ドライバー情報取得エラー";
        }

        return device;
    }

    private static void ApplyDriverInfo(DeviceInfo device, Dictionary<string, SignedDriver> drivers)
    {
        if (drivers.TryGetValue(device.DeviceId, out var driver))
        {
            device.DriverVersion = driver.Version;
            if (driver.RawDate != null)
                device.DriverDate = ManagementDateTimeConverter.ToDateTime(driver.RawDate).ToString("yyyy/MM/dd");
        }

        if (device.DriverVersion == Unknown && ReadServiceDriverInfo(device.Service) is { } info)
        {
            if (info.Version != Unknown) device.DriverVersion = info.Version;
            device.DriverDate = info.Date;
        }
    }

    /// <summary>
    /// サービス名から、カーネルドライバーのファイル（.sys）のバージョンと更新日を読む。
    /// ドライバー情報（Win32_PnPSignedDriver）が無いデバイスの代わりに使う。
    /// ドライバーでない場合や、ファイルが無い場合は null を返す。
    /// </summary>
    public static (string Version, string Date)? ReadServiceDriverInfo(string service)
    {
        if (string.IsNullOrWhiteSpace(service) || service == Unknown || service.IndexOfAny(['\\', '/']) >= 0) return null;

        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = hklm.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{service}");
        if (key?.GetValue("Type") is not int type || type is not (ServiceTypeKernelDriver or ServiceTypeFileSystemDriver))
            return null;

        var image = PathResolver.ResolveDriverImagePath(
            key.GetValue("ImagePath") as string, service, Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        if (!File.Exists(image)) return null;

        var version = FileVersionInfo.GetVersionInfo(image).FileVersion;
        return (string.IsNullOrWhiteSpace(version) ? Unknown : version.Trim(), File.GetLastWriteTime(image).ToString("yyyy/MM/dd"));
    }
    /// <summary>Windows Updateにドライバーの更新を検索させ、ダウンロードとインストールまで行うPowerShellのスクリプト。</summary>
    private const string DriverUpdateScript = @"
                $UpdateSession = New-Object -ComObject Microsoft.Update.Session
                $UpdateSearcher = $UpdateSession.CreateUpdateSearcher()
                Write-Host 'ドライバー更新を検索中…'
                $SearchResult = $UpdateSearcher.Search('IsInstalled=0 and Type=''Driver''')

                if ($SearchResult.Updates.Count -eq 0) {
                    Write-Host '利用可能なドライバー更新はありません。'
                    exit 0
                }

                Write-Host ""$($SearchResult.Updates.Count)件のドライバー更新が見つかりました。""

                $UpdatesToInstall = New-Object -ComObject Microsoft.Update.UpdateColl
                foreach ($Update in $SearchResult.Updates) {
                    Write-Host ""  - $($Update.Title)""
                    $UpdatesToInstall.Add($Update) | Out-Null
                }

                Write-Host 'ダウンロード中…'
                $Downloader = $UpdateSession.CreateUpdateDownloader()
                $Downloader.Updates = $UpdatesToInstall
                $Downloader.Download() | Out-Null

                Write-Host 'インストール中…'
                $Installer = $UpdateSession.CreateUpdateInstaller()
                $Installer.Updates = $UpdatesToInstall
                $InstallResult = $Installer.Install()

                if ($InstallResult.RebootRequired) {
                    Write-Host '再起動が必要です。'
                }

                Write-Host '完了しました。'
                exit 0
";

    public async Task<(bool success, string message)> RunWindowsUpdateAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = SystemTools.PowerShell,
                    Arguments = $"-ExecutionPolicy Bypass -Command \"{DriverUpdateScript.Replace("\"", "\\\"")}\"",
                    UseShellExecute = true,
                    Verb = "runas"
                };

                using var process = Process.Start(processInfo);
                process?.WaitForExit();

                return process?.ExitCode == 0
                    ? (true, "Windows Updateが完了しました。")
                    : (false, "Windows Updateの実行中にエラーが発生しました。");
            }
            catch (Exception ex)
            {
                return (false, $"ドライバーの更新に失敗しました（詳細: {ex.Message}）");
            }
        });
    }

    /// <summary>Windows Update の設定画面を開く。開けなかったときは false を返す。</summary>
    public bool OpenWindowsUpdateSettings()
    {
        // 「オプションの更新」の画面を優先し、開けなければ Windows Update の画面を開く
        foreach (var uri in new[] { "ms-settings:windowsupdate-optionalupdates", "ms-settings:windowsupdate" })
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = uri, UseShellExecute = true });
                return true;
            }
            catch (Win32Exception)
            {
                // 次の候補を試す
            }
        }
        return false;
    }
}
