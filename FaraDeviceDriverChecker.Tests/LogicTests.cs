using FaraDeviceDriverChecker.Services;
using FaraDeviceDriverChecker.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace FaraDeviceDriverChecker.Tests;

public class LogicTests
{
    private readonly ITestOutputHelper _output;

    public LogicTests(ITestOutputHelper output) => _output = output;

    // 圧縮 GUID は先頭 3 区画を文字単位で反転、残りを 2 文字ずつ入れ替えたもの
    [Theory]
    [InlineData("{409CB30E-E457-4008-9B1A-ED1B9EA21140}", "E03BC904754E8004B9A1DEB1E92A1104")]
    public void Pack_と_Unpack_は相互変換できる(string guid, string packed)
    {
        Assert.Equal(packed, MsiGuid.Pack(guid));
        Assert.Equal(guid, MsiGuid.Unpack(packed));
    }

    [Fact]
    public void RST_の_Products_キーは_圧縮_GUID_として戻せる()
    {
        var guid = MsiGuid.Unpack("9732161F3A3881F4B8B9A74A3A391E60");
        Assert.NotNull(guid);
        Assert.Equal("9732161F3A3881F4B8B9A74A3A391E60", MsiGuid.Pack(guid!));
    }

    [Theory]
    [InlineData("iaAHCIC.inf", true)]
    [InlineData("iaStorVD.inf", true)]
    [InlineData("stornvme.inf", true)]
    [InlineData("nvidia.inf", false)]
    [InlineData("netrtwlane.inf", false)]
    public void ストレージ系の名前を判定できる(string name, bool expected)
        => Assert.Equal(expected, StorageGuard.IsStorageName(name));

    [Fact]
    public void ストレージ系のクラス_GUID_を判定できる()
    {
        Assert.True(StorageGuard.IsStorageClass("{4D36E97B-E325-11CE-BFC1-08002BE10318}"));
        Assert.False(StorageGuard.IsStorageClass("{4d36e972-e325-11ce-bfc1-08002be10318}"));
    }

    [Fact]
    public void pnputil_の出力を日本語ラベルでも英語ラベルでも解析できる()
    {
        const string ja = "Microsoft PnP ユーティリティ\r\n\r\n" +
            "公開名:             oem12.inf\r\n元の名前:           iaahcic.inf\r\nプロバイダー名:     Intel\r\n" +
            "クラス名:           ハード ディスク コントローラー\r\nクラス GUID:       {4d36e96a-e325-11ce-bfc1-08002be10318}\r\n" +
            "ドライバーのバージョン: 06/21/2006 10.1.18.0\r\n署名者名:         Microsoft Windows Hardware Compatibility Publisher\r\n\r\n" +
            "公開名:             oem40.inf\r\n元の名前:           iaahcic.inf\r\nプロバイダー名:     Intel\r\n" +
            "クラス名:           ハード ディスク コントローラー\r\nクラス GUID:       {4d36e96a-e325-11ce-bfc1-08002be10318}\r\n" +
            "ドライバーのバージョン: 03/01/2023 19.0.0.1\r\n署名者名:         x\r\n";
        var packages = PnpUtilParser.Parse(ja);
        Assert.Equal(2, packages.Count);
        Assert.Equal("oem12.inf", packages[0].PublishedName);
        Assert.Equal("iaahcic.inf", packages[0].OriginalName);
        Assert.Equal("Intel", packages[0].Provider);
        Assert.Equal(new Version(19, 0, 0, 1), packages[1].Version);
        Assert.True(StorageGuard.IsStorageClass(packages[0].ClassGuid));

        var en = ja.Replace("公開名:", "Published Name:").Replace("元の名前:", "Original Name:");
        Assert.Equal(2, PnpUtilParser.Parse(en).Count);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\Foo\\unins000.exe\" /S", "C:\\Program Files\\Foo\\unins000.exe")]
    [InlineData("C:\\Program Files\\Foo Bar\\setup.exe /uninstall", "C:\\Program Files\\Foo Bar\\setup.exe")]
    [InlineData("MsiExec.exe /X{409CB30E-E457-4008-9B1A-ED1B9EA21140}", "MsiExec.exe")]
    [InlineData("C:\\a\\b.cmd", "C:\\a\\b.cmd")]
    [InlineData("\"\"", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("rundll32 foo.dll,Remove", null)]
    public void コマンドラインから実行ファイルのパスを取り出せる(string? commandLine, string? expected)
        => Assert.Equal(expected, PathResolver.ExtractExePath(commandLine));

    [Theory]
    [InlineData(null, "foo", "C:\\Windows\\System32\\drivers\\foo.sys")]
    [InlineData("", "foo", "C:\\Windows\\System32\\drivers\\foo.sys")]
    [InlineData("System32\\DRIVERS\\foo.sys", "foo", "C:\\Windows\\System32\\DRIVERS\\foo.sys")]
    [InlineData("\\SystemRoot\\System32\\drivers\\foo.sys", "foo", "C:\\Windows\\System32\\drivers\\foo.sys")]
    [InlineData("\\??\\C:\\Program Files\\Vendor\\foo.sys", "foo", "C:\\Program Files\\Vendor\\foo.sys")]
    [InlineData("D:\\drv\\foo.sys", "foo", "D:\\drv\\foo.sys")]
    [InlineData("\"D:\\drv dir\\foo.sys\"", "foo", "D:\\drv dir\\foo.sys")]
    public void ドライバーの_ImagePath_を絶対パスに解決できる(string? imagePath, string name, string expected)
        => Assert.Equal(expected, PathResolver.ResolveDriverImagePath(imagePath, name, "C:\\Windows"));

    [Fact]
    public void ImagePath_の環境変数は展開される()
    {
        var systemRoot = Environment.ExpandEnvironmentVariables("%SystemRoot%");
        var actual = PathResolver.ResolveDriverImagePath("%SystemRoot%\\System32\\drivers\\foo.sys", "foo", "C:\\Windows");
        Assert.Equal(systemRoot + "\\System32\\drivers\\foo.sys", actual);
    }

    [Theory]
    [InlineData("HKCU\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Ozone 9 Elements", true)]
    [InlineData("HKLM\\SOFTWARE\\Foo & Bar", true)]
    [InlineData("x\" & calc & \"y", false)]
    [InlineData("x$(calc)", false)]
    [InlineData("x`ncalc", false)]
    [InlineData("%PATH%", false)]
    [InlineData("a\u201Cb", false)]
    [InlineData("a\r\nb", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void 二重引用符の中に埋め込んでよい値だけを許可する(string? value, bool expected)
        => Assert.Equal(expected, CommandGuard.IsSafeInDoubleQuotes(value));

    [Theory]
    [InlineData("C:\\Program Files\\Intel\\Intel(R) Rapid Storage Technology", true)]
    [InlineData("HID\\VID_1532&PID_0306&MI_00&COL00\\2&1AF591FA&1&0000", true)]
    [InlineData("C:\\a'; calc; '", false)]
    [InlineData("C:\\a\u2019b", false)]
    [InlineData("C:\\a\nb", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void 単一引用符の中に埋め込んでよい値だけを許可する(string? value, bool expected)
        => Assert.Equal(expected, CommandGuard.IsSafeInSingleQuotes(value));

    /// <summary>実機で全チェックを実行し、例外なく完了することと結果を確認する（読み取り専用）。</summary>
    [Fact]
    public void 実機で残骸チェックが完了する()
    {
        var result = new LeftoverScanner().Scan();
        _output.WriteLine($"管理者={result.IsAdministrator} 件数={result.Items.Count}");
        foreach (var i in result.Items)
            _output.WriteLine($"[{i.SeverityDisplay}] {i.Category} | {i.Title} | {i.Target}");
        Assert.DoesNotContain(result.Items, i => i.Title.Contains("の確認に失敗しました"));

        // ストレージ系は案内コマンドを出さない
        Assert.All(result.Items.Where(i => i.IsProtected), i => Assert.Equal("", i.CommandDisplay.Contains("提案しません") ? "" : i.CommandDisplay));
        Assert.DoesNotContain(result.Items, i => i.SuggestedCommand.Contains("delete-driver", StringComparison.OrdinalIgnoreCase)
            && StorageGuard.IsStorageName(i.Target));

        var json = ReportExporter.ToJson([], result);
        Assert.Contains("leftover", json);
        Assert.NotEmpty(ReportExporter.ToText([], result));
    }
}
