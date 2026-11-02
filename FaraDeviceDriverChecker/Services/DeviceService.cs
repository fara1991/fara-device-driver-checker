namespace FaraDeviceDriverChecker.Services;

using System.Diagnostics;
using System.Management;
using Models;
using Utilities;

public class DeviceService
{
    public List<DeviceInfo> GetDevices(string[] deviceClasses)
    {
        var devices = new List<DeviceInfo>();
        var deviceIds = GetDeviceIdsByClasses(deviceClasses);

        foreach (var deviceId in deviceIds)
        {
            var deviceInfo = GetDeviceInfoById(deviceId);
            if (deviceInfo != null)
            {
                devices.Add(deviceInfo);
            }
        }

        return RemoveDuplicates(devices);
    }

    private List<string> GetDeviceIdsByClasses(string[] deviceClasses)
    {
        var deviceIds = new List<string>();

        foreach (var deviceClass in deviceClasses)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "pnputil",
                    Arguments = $"/enum-devices /class {deviceClass}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process == null) continue;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.Contains("インスタンス ID:") || line.Contains("Instance ID:"))
                    {
                        var id = line.Split(':').LastOrDefault()?.Trim();
                        if (!string.IsNullOrEmpty(id))
                        {
                            deviceIds.Add(id);
                        }
                    }
                }
            }
            catch
            {
                // エラー時は継続
            }
        }

        return deviceIds;
    }

    private DeviceInfo? GetDeviceInfoById(string deviceId)
    {
        try
        {
            var escapedDeviceId = WmiHelper.EscapeWqlString(deviceId);
            var query = $"SELECT * FROM Win32_PnPEntity WHERE DeviceID = '{escapedDeviceId}'";

            using var searcher = new ManagementObjectSearcher(query);
            var collection = searcher.Get();

            foreach (var o in collection)
            {
                var device = (ManagementObject)o;
                var deviceInfo = new DeviceInfo
                {
                    Name = WmiHelper.GetPropertyValue(device, "Name"),
                    DeviceId = WmiHelper.GetPropertyValue(device, "DeviceID"),
                    Description = WmiHelper.GetPropertyValue(device, "Description"),
                    Manufacturer = WmiHelper.GetPropertyValue(device, "Manufacturer"),
                    Status = WmiHelper.GetPropertyValue(device, "Status"),
                    Class = WmiHelper.GetPropertyValue(device, "PNPClass"),
                    Service = WmiHelper.GetPropertyValue(device, "Service"),
                    HardwareId = WmiHelper.GetPropertyValue(device, "HardwareID")
                };

                SetProblemCode(device, deviceInfo);
                GetDriverInfo(deviceInfo);
                return deviceInfo;
            }
        }
        catch
        {
            // エラー時はnullを返す
        }

        return null;
    }

    public async Task<(bool success, string message)> RunWindowsUpdateAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var script = @"
                $UpdateSession = New-Object -ComObject Microsoft.Update.Session
                $UpdateSearcher = $UpdateSession.CreateUpdateSearcher()
                Write-Host 'ドライバー更新を検索中…'
                $SearchResult = $UpdateSearcher.Search('IsInstalled=0 and Type=''Driver''')

                if ($SearchResult.Updates.Count -eq 0) {
                    Write-Host '利用可能なドライバー更新はありません。'
                    exit 0
                }

                Write-Host ""$($SearchResult.Updates.Count) 件のドライバー更新が見つかりました。""

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

                var processInfo = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"",
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

    public void OpenWindowsUpdateSettings()
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "ms-settings:windowsupdate-optionalupdates",
                UseShellExecute = true
            };
            Process.Start(processInfo);
        }
        catch
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "ms-settings:windowsupdate",
                UseShellExecute = true
            };
            Process.Start(processInfo);
        }
    }

    private static void SetProblemCode(ManagementObject device, DeviceInfo deviceInfo)
    {
        if (device["ConfigManagerErrorCode"] == null) return;

        var problemCode = (uint)device["ConfigManagerErrorCode"];
        deviceInfo.HasProblem = problemCode != 0;
        deviceInfo.ProblemCode = ErrorCodeHelper.GetProblemDescription(problemCode);
    }

    private static void GetDriverInfo(DeviceInfo device)
    {
        try
        {
            var escapedDeviceId = WmiHelper.EscapeWqlString(device.DeviceId);
            var query = $"SELECT * FROM Win32_PnPSignedDriver WHERE DeviceID = '{escapedDeviceId}'";

            using var searcher = new ManagementObjectSearcher(query);
            var collection = searcher.Get();

            foreach (var o in collection)
            {
                var driver = (ManagementObject)o;
                device.DriverVersion = driver["DriverVersion"]?.ToString() ?? "不明";

                if (driver["DriverDate"] != null)
                {
                    var driverDate = ManagementDateTimeConverter.ToDateTime(driver["DriverDate"].ToString());
                    device.DriverDate = driverDate.ToString("yyyy/MM/dd");
                }

                break;
            }

            if (device.DriverVersion == "不明" && !string.IsNullOrEmpty(device.Service))
            {
                GetSystemDriverInfo(device);
            }
        }
        catch (Exception ex)
        {
            device.DriverVersion = $"取得に失敗しました（詳細: {ex.Message}）";
            device.HasProblem = true;
            device.ProblemCode = "ドライバー情報取得エラー";
        }
    }

    private static void GetSystemDriverInfo(DeviceInfo device)
    {
        var escapedService = WmiHelper.EscapeWqlString(device.Service);
        var query = $"SELECT * FROM Win32_SystemDriver WHERE Name = '{escapedService}'";

        using var driverSearcher = new ManagementObjectSearcher(query);
        var driverCollection = driverSearcher.Get();

        foreach (var o in driverCollection)
        {
            var driver = (ManagementObject)o;
            device.DriverVersion = driver["Version"]?.ToString() ?? "不明";

            if (driver["InstallDate"] != null)
            {
                var installDate = ManagementDateTimeConverter.ToDateTime(driver["InstallDate"].ToString());
                device.DriverDate = installDate.ToString("yyyy/MM/dd");
            }

            break;
        }
    }

    private static List<DeviceInfo> RemoveDuplicates(List<DeviceInfo> devices)
    {
        var seenDeviceIds = new HashSet<string>();
        return devices.Where(device => seenDeviceIds.Add(device.DeviceId)).ToList();
    }
}
