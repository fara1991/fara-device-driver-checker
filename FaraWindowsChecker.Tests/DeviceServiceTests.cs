using FaraWindowsChecker.Services;
using FaraWindowsChecker.Utilities;
using Xunit;

namespace FaraWindowsChecker.Tests;

public class DeviceServiceTests
{
    [Fact]
    public void pnputil_の出力から_日本語でも英語でも_インスタンスIDを取り出せる()
    {
        const string ja = "Microsoft PnP ユーティリティ\r\n\r\n" +
            "インスタンス ID:    HDAUDIO\\FUNC_01&VEN_10EC&DEV_0899\\5&2A7B4C1&0&0001\r\n" +
            "デバイスの説明:     High Definition Audio\r\n\r\n" +
            "インスタンス ID:    USB\\VID_2A39&PID_3FC0\\6&1A2B3C&0&2\r\n";
        var en = ja.Replace("インスタンス ID:", "Instance ID:");

        var expected = new[] { "HDAUDIO\\FUNC_01&VEN_10EC&DEV_0899\\5&2A7B4C1&0&0001", "USB\\VID_2A39&PID_3FC0\\6&1A2B3C&0&2" };
        Assert.Equal(expected, DeviceService.ParseInstanceIds(ja));
        Assert.Equal(expected, DeviceService.ParseInstanceIds(en));
        Assert.Empty(DeviceService.ParseInstanceIds(""));
    }

    [Fact]
    public void 外部の実行ファイルは_System32_の絶対パスで指定する()
    {
        Assert.True(File.Exists(SystemTools.PnpUtil), SystemTools.PnpUtil);
        Assert.True(File.Exists(SystemTools.PowerShell), SystemTools.PowerShell);
    }

    [Fact]
    public void サービス名から_カーネルドライバーのバージョンと更新日を読める()
    {
        var info = DeviceService.ReadServiceDriverInfo("disk");

        Assert.NotNull(info);
        Assert.False(string.IsNullOrWhiteSpace(info.Value.Version));
        Assert.Matches(@"^\d{4}/\d{2}/\d{2}$", info.Value.Date);
    }

    [Theory]
    [InlineData("")]
    [InlineData("不明")]
    [InlineData("存在しないサービス_xyz")]
    [InlineData("..\\Foo")]
    [InlineData("Foo/Bar")]
    [InlineData("EventLog")] // ドライバーではなくサービス（Type=32）
    public void ドライバーではない_または見つからないサービス名は_null_を返す(string service)
        => Assert.Null(DeviceService.ReadServiceDriverInfo(service));

    /// <summary>実機で、全件を1回で取得する方式が、短い時間で完了することを確認する（読み取り専用）。</summary>
    [Fact]
    public void 実機でデバイスの一覧を数十秒以内に取得できる()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = DeviceService.GetDevices(["Media", "AudioEndpoint"]);
        sw.Stop();

        Assert.Equal(0, result.FailedCount);
        Assert.NotEmpty(result.Devices);
        Assert.All(result.Devices, d =>
        {
            Assert.False(string.IsNullOrEmpty(d.DeviceId));
            Assert.DoesNotContain("System.String[]", d.HardwareId);
        });
        Assert.Equal(result.Devices.Count, result.Devices.Select(d => d.DeviceId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"{sw.Elapsed.TotalSeconds:N1} 秒かかりました");
    }
}
