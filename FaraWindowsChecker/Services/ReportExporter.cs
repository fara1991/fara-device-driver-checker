namespace FaraWindowsChecker.Services;

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Models;
using Utilities;

/// <summary>診断結果（ドライバー診断・残骸チェック）をテキストまたはJSONに書き出す。</summary>
public static class ReportExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string ToText(IReadOnlyList<DeviceInfo> devices, LeftoverScanResult? leftover)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{AppInfo.Name}診断レポート");
        sb.AppendLine($"バージョン: {AppInfo.DisplayVersion}");
        sb.AppendLine($"出力日時: {DateTime.Now:yyyy/MM/dd HH:mm:ss}");
        sb.AppendLine($"OS: {Environment.OSVersion}");
        sb.AppendLine();

        if (devices.Count > 0)
        {
            sb.AppendLine($"■ ドライバー診断（{devices.Count}件 / 問題 {devices.Count(d => d.HasProblem)}件）");
            foreach (var d in devices)
            {
                sb.AppendLine($"- [{d.StatusDisplay}] {d.Name} | {d.Manufacturer} | {d.Class} | {FormatVersion(d.DriverVersion)} | {d.DriverDate}");
                sb.AppendLine($"    DeviceID: {d.DeviceId}");
            }
            sb.AppendLine();
        }

        if (leftover != null)
        {
            sb.AppendLine($"■ アンインストール漏れ（残骸）チェック（{leftover.Items.Count}件）");
            sb.AppendLine($"スキャン日時: {leftover.ScannedAt:yyyy/MM/dd HH:mm:ss} / 管理者権限: {(leftover.IsAdministrator ? "あり" : "なし")}");
            sb.AppendLine("※ 検出のみで、削除などの変更は行っていません。案内コマンドは内容を確認したうえで手動で実行してください。");
            sb.AppendLine();
            foreach (var group in leftover.Items.GroupBy(i => i.Category))
            {
                sb.AppendLine($"【{group.Key}】");
                foreach (var i in group)
                {
                    sb.AppendLine($"- [{i.SeverityDisplay}] {i.Title}");
                    sb.AppendLine($"    対象: {i.Target}");
                    if (i.Detail != "") sb.AppendLine($"    今の状態: {i.Detail}");
                    if (i.Impact != "") sb.AppendLine($"    放置した場合の影響: {i.Impact}");
                    if (i.Action != "") sb.AppendLine($"    推奨する対応（{i.ActionSummary}）: {i.Action}");
                    if (i.CommandDisplay != "")
                    {
                        sb.AppendLine("    案内コマンド:");
                        foreach (var line in i.CommandDisplay.Split(["\r\n", "\n"], StringSplitOptions.None))
                            sb.AppendLine($"      {line}");
                    }
                }
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>数字で始まる版だけに「v」を付ける（「不明」などに付けて「v不明」にならないようにする）。</summary>
    public static string FormatVersion(string version)
        => version.Length > 0 && char.IsDigit(version[0]) ? $"v{version}" : version;

    public static string ToJson(IReadOnlyList<DeviceInfo> devices, LeftoverScanResult? leftover)
    {
        var report = new
        {
            app = AppInfo.Name,
            version = AppInfo.Version,
            commit = AppInfo.Commit,
            exportedAt = DateTime.Now,
            os = Environment.OSVersion.ToString(),
            devices = devices.Select(d => new
            {
                d.Name, d.DeviceId, d.Manufacturer, d.Class, d.DriverVersion, d.DriverDate,
                d.HasProblem, problem = d.ProblemCode, d.HardwareId
            }),
            leftover = leftover == null ? null : new
            {
                scannedAt = leftover.ScannedAt,
                isAdministrator = leftover.IsAdministrator,
                items = leftover.Items.Select(i => new
                {
                    i.Category,
                    severity = i.SeverityDisplay,
                    i.Title,
                    i.Target,
                    i.Detail,
                    impact = i.Impact,
                    action = i.Action,
                    actionSummary = i.ActionSummary,
                    i.IsProtected,
                    suggestedCommand = i.IsProtected ? "" : i.SuggestedCommand
                })
            }
        };
        return JsonSerializer.Serialize(report, JsonOptions);
    }
}
