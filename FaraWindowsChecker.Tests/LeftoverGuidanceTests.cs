using System.Reflection;
using FaraWindowsChecker.Models;
using FaraWindowsChecker.Services;
using Xunit;

namespace FaraWindowsChecker.Tests;

public class LeftoverGuidanceTests
{
    /// <summary>固定の案内（static フィールド）と、条件で変わる案内（static メソッド）をすべて集める。</summary>
    public static IEnumerable<object[]> AllGuidance()
    {
        var type = typeof(LeftoverGuidance);
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == type))
            yield return [field.Name, (LeftoverGuidance)field.GetValue(null)!];

        foreach (var flag in new[] { false, true })
        {
            yield return [$"MsiInfNotInStore({flag})", LeftoverGuidance.MsiInfNotInStore(flag)];
            yield return [$"DeviceReferencesMissingInf({flag})", LeftoverGuidance.DeviceReferencesMissingInf(flag)];
            foreach (var other in new[] { false, true })
            {
                yield return [$"DuplicateDriver({flag},{other})", LeftoverGuidance.DuplicateDriver(flag, other)];
                yield return [$"KernelDriverServiceMissing({flag},{other})", LeftoverGuidance.KernelDriverServiceMissing(flag, other)];
            }
        }

        foreach (var code in new uint[] { 0, 1, 3, 10, 12, 18, 22, 28, 31, 37, 39, 43, 45, 99 })
            yield return [$"ProblemDevice({code})", LeftoverGuidance.ProblemDevice(code)];
    }

    [Theory]
    [MemberData(nameof(AllGuidance))]
    public void すべての案内に_対応の目安_影響_対応が入っている(string name, LeftoverGuidance guidance)
    {
        Assert.False(string.IsNullOrWhiteSpace(guidance.Summary), $"{name}: 対応の目安が空です");
        Assert.False(string.IsNullOrWhiteSpace(guidance.Impact), $"{name}: 影響が空です");
        Assert.False(string.IsNullOrWhiteSpace(guidance.Action), $"{name}: 対応が空です");

        // 一覧の列に収まる長さ
        Assert.True(guidance.Summary.Length <= 12, $"{name}: 対応の目安が長すぎます（{guidance.Summary.Length}文字）");
        // 説明文は、句点で終わる
        Assert.EndsWith("。", guidance.Impact);
        Assert.EndsWith("。", guidance.Action);
        // 日本語と英数字の間に空白を入れない
        Assert.DoesNotMatch(@"[ぁ-んァ-ン一-龥] [A-Za-z0-9]|[A-Za-z0-9] [ぁ-んァ-ン一-龥]", guidance.Impact + guidance.Action);
    }

    [Fact]
    public void ストレージ系の案内は_手動で削除しないよう伝える()
    {
        Assert.Contains("手動で削除", LeftoverGuidance.MsiInfNotInStore(isStorage: true).Action);
        Assert.Contains("ストレージ系", LeftoverGuidance.DeviceReferencesMissingInf(isStorage: true).Action);
        Assert.Contains("削除しないでください", LeftoverGuidance.DuplicateDriver(used: false, isStorage: true).Action);
        Assert.Contains("手動で削除", LeftoverGuidance.KernelDriverServiceMissing(loadedAtBoot: true, isStorage: true).Action);

        // ストレージ系でないときは、削除を案内する
        Assert.Contains("pnputil /delete-driver", LeftoverGuidance.DuplicateDriver(used: false, isStorage: false).Action);
    }

    [Fact]
    public void 起動時に読み込まれるドライバーの登録は_起動への影響を伝える()
    {
        Assert.Contains("起動", LeftoverGuidance.KernelDriverServiceMissing(loadedAtBoot: true, isStorage: false).Impact);
        Assert.Contains("実害はありません", LeftoverGuidance.KernelDriverServiceMissing(loadedAtBoot: false, isStorage: false).Impact);
    }

    [Fact]
    public void エクスポートに_今の状態_影響_対応が含まれる()
    {
        var item = new LeftoverItem
        {
            Category = "サービスの残り",
            Severity = LeftoverSeverity.Review,
            Title = "実行ファイルの無いサービス: Foo",
            Target = "Foo",
            Detail = "状態: Stopped",
            Guidance = LeftoverGuidance.ServiceMissingExe
        };
        var result = new LeftoverScanResult { Items = [item] };

        var text = ReportExporter.ToText([], result);
        Assert.Contains("今の状態: 状態: Stopped", text);
        Assert.Contains("放置した場合の影響: ", text);
        Assert.Contains("推奨する対応（削除を検討）: ", text);

        var json = ReportExporter.ToJson([], result);
        Assert.Contains("\"impact\"", json);
        Assert.Contains("\"action\"", json);
        Assert.Contains("\"actionSummary\": \"削除を検討\"", json);
    }

    [Fact]
    public void 案内の無い項目でも_空の文字列として扱える()
    {
        var item = new LeftoverItem { Category = "c", Severity = LeftoverSeverity.Info, Title = "t", Target = "x" };

        Assert.Equal("", item.ActionSummary);
        Assert.Equal("", item.Impact);
        Assert.Equal("", item.Action);
    }
}
