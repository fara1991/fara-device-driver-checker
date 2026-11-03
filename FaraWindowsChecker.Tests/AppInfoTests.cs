using System.Reflection;
using FaraWindowsChecker.Services;
using FaraWindowsChecker.Utilities;
using Xunit;

namespace FaraWindowsChecker.Tests;

public class AppInfoTests
{
    [Theory]
    [InlineData("1.2.3+abcdef0123456789", "1.2.3")]
    [InlineData("1.2.3-beta.1+deadbeef", "1.2.3-beta.1")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3+", "1.2.3")]
    [InlineData("0.0.0-dev+1a2b3c4d", "0.0.0-dev")]
    [InlineData("", "0.0.0-dev")]
    [InlineData("   ", "0.0.0-dev")]
    [InlineData(null, "0.0.0-dev")]
    [InlineData("+abcdef0", "0.0.0-dev")]
    public void バージョン情報から_版の番号だけを取り出せる(string? informationalVersion, string expected)
        => Assert.Equal(expected, AppInfo.ParseInformationalVersion(informationalVersion));

    [Theory]
    [InlineData("1.2.3", "v1.2.3")]
    [InlineData("1.2.3-beta.1", "v1.2.3-beta.1")]
    [InlineData("0.0.0-dev", "開発版")]
    public void 表示用のバージョンは_開発版と_リリース版で書き分ける(string version, string expected)
        => Assert.Equal(expected, AppInfo.FormatDisplayVersion(version));

    [Fact]
    public void ビルドに埋め込まれたバージョンを読める()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.Version));
        Assert.DoesNotContain("+", AppInfo.Version);
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.DisplayVersion));
    }

    /// <summary>コミットの番号を、アプリに残さない。ビルドのバージョン情報にも、「+コミット」が付いていないこと。</summary>
    [Fact]
    public void アプリのバージョン情報に_コミットの番号が含まれない()
    {
        var informational = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        Assert.False(string.IsNullOrEmpty(informational));
        Assert.DoesNotContain("+", informational);
        Assert.DoesNotContain("コミット", AppInfo.DisplayVersion);
    }

    [Fact]
    public void エクスポートに_バージョンが含まれ_コミットは含まれない()
    {
        var text = ReportExporter.ToText([], null);
        var json = ReportExporter.ToJson([], null);

        Assert.Contains($"バージョン: {AppInfo.DisplayVersion}", text);
        Assert.Contains($"\"version\": \"{AppInfo.Version}\"", json);
        Assert.Contains($"\"app\": \"{AppInfo.Name}\"", json);
        Assert.DoesNotContain("commit", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("コミット", text);
    }
}
