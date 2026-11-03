using FaraWindowsChecker.Services;
using FaraWindowsChecker.Utilities;
using Xunit;

namespace FaraWindowsChecker.Tests;

public class AppInfoTests
{
    [Theory]
    [InlineData("1.2.3+abcdef0123456789", "1.2.3", "abcdef0")]
    [InlineData("1.2.3-beta.1+deadbeef", "1.2.3-beta.1", "deadbee")]
    [InlineData("1.2.3", "1.2.3", null)]
    [InlineData("1.2.3+abc", "1.2.3", "abc")]
    [InlineData("1.2.3+", "1.2.3", null)]
    [InlineData("0.0.0-dev+1a2b3c4d", "0.0.0-dev", "1a2b3c4")]
    [InlineData("", "0.0.0-dev", null)]
    [InlineData(null, "0.0.0-dev", null)]
    [InlineData("+abcdef0", "0.0.0-dev", "abcdef0")]
    public void バージョン情報を_番号とコミットに分けられる(string? informationalVersion, string expectedVersion, string? expectedCommit)
    {
        var (version, commit) = AppInfo.ParseInformationalVersion(informationalVersion);

        Assert.Equal(expectedVersion, version);
        Assert.Equal(expectedCommit, commit);
    }

    [Theory]
    [InlineData("1.2.3", "abcdef0", "v1.2.3（コミット: abcdef0）")]
    [InlineData("1.2.3", null, "v1.2.3")]
    [InlineData("0.0.0-dev", "abcdef0", "開発版（コミット: abcdef0）")]
    [InlineData("0.0.0-dev", null, "開発版")]
    public void 表示用のバージョンは_開発版と_リリース版で書き分ける(string version, string? commit, string expected)
        => Assert.Equal(expected, AppInfo.FormatDisplayVersion(version, commit));

    [Fact]
    public void ビルドに埋め込まれたバージョンを読める()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.Version));
        Assert.DoesNotContain("+", AppInfo.Version);
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.DisplayVersion));
    }

    [Fact]
    public void エクスポートに_バージョンが含まれる()
    {
        var text = ReportExporter.ToText([], null);
        var json = ReportExporter.ToJson([], null);

        Assert.Contains($"バージョン: {AppInfo.DisplayVersion}", text);
        Assert.Contains("\"version\"", json);
        Assert.Contains($"\"version\": \"{AppInfo.Version}\"", json);
        Assert.Contains($"\"app\": \"{AppInfo.Name}\"", json);
    }
}
