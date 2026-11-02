using FaraDeviceDriverChecker.Utilities;
using Xunit;

namespace FaraDeviceDriverChecker.Tests;

public class CommandBuilderTests
{
    private const string UnsafeValue = "x\" & calc & \"y";

    [Fact]
    public void レジストリの削除コマンドは_バックアップの後に削除する()
    {
        var command = CommandBuilder.DeleteWithBackupCommand("HKLM\\SOFTWARE\\Foo", "backup-foo");

        var lines = command.Split("\r\n");
        Assert.Equal("reg export \"HKLM\\SOFTWARE\\Foo\" \"backup-foo.reg\"", lines[0]);
        Assert.Equal("reg delete \"HKLM\\SOFTWARE\\Foo\" /f", lines[1]);
    }

    [Fact]
    public void 引用符を抜け出せる値のときは_コマンドを作らない()
    {
        Assert.Equal("", CommandBuilder.DeleteWithBackupCommand(UnsafeValue, "backup-foo"));
        Assert.Equal("", CommandBuilder.DeleteWithBackupCommand("HKLM\\SOFTWARE\\Foo", UnsafeValue));
        Assert.Equal("", CommandBuilder.ServiceCommand(UnsafeValue));
        Assert.Equal("", CommandBuilder.RemoveFolderCommand("C:\\a'; calc; '", "-Recurse"));
        Assert.Equal("", CommandBuilder.GetPnpDeviceCommand("a'b"));
    }

    [Fact]
    public void デバイスIDの_アンパサンドは許可する()
    {
        var command = CommandBuilder.GetPnpDeviceCommand("HID\\VID_1532&PID_0306&MI_00\\2&1AF591FA&1&0000");

        Assert.Equal("Get-PnpDevice -InstanceId 'HID\\VID_1532&PID_0306&MI_00\\2&1AF591FA&1&0000' | Format-List *", command);
    }

    [Fact]
    public void サービスのコマンドは_確認と削除を並べる()
        => Assert.Equal("sc.exe qc \"Foo\"\r\nsc.exe delete \"Foo\"", CommandBuilder.ServiceCommand("Foo"));

    [Fact]
    public void 空のコマンドは連結から除く()
    {
        Assert.Equal("a\r\nb", CommandBuilder.JoinCommands("a", "", "b"));
        Assert.Equal("", CommandBuilder.JoinCommands("", ""));
    }

    [Theory]
    [InlineData("oem12.inf", "pnputil /delete-driver oem12.inf")]
    [InlineData("OEM3.INF", "pnputil /delete-driver OEM3.INF")]
    [InlineData("oem12.inf & calc", "")]
    [InlineData("oem12.inf\ncalc", "")]
    [InlineData("oem12.inf\n", "")] // $ は末尾の改行の前にも一致するため、\z で厳密に判定する
    [InlineData("iaahcic.inf", "")]
    [InlineData("", "")]
    public void 削除コマンドは_公開名の形のときだけ作る(string publishedName, string expected)
        => Assert.Equal(expected, CommandBuilder.DeleteDriverCommand(publishedName));
}
