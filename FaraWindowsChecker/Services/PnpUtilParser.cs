namespace FaraWindowsChecker.Services;

using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>ドライバーストア上の 1 パッケージ（pnputil /enum-drivers の 1 レコード）。</summary>
public record DriverPackage(
    string PublishedName,
    string OriginalName,
    string Provider,
    string ClassName,
    string ClassGuid,
    DateTime? Date,
    Version? Version);

/// <summary>
/// pnputil /enum-drivers の出力を解析する。
/// 項目名は OS の表示言語で変わるため、項目名ではなく値の形式と出現順で判定する。
/// </summary>
public static partial class PnpUtilParser
{
    [GeneratedRegex(@"^oem\d+\.inf$", RegexOptions.IgnoreCase)]
    private static partial Regex PublishedPattern();

    [GeneratedRegex(@"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}")]
    private static partial Regex GuidPattern();

    [GeneratedRegex(@"(\d{1,2}/\d{1,2}/\d{4})\s+(\d+(?:\.\d+){1,3})")]
    private static partial Regex VersionPattern();

    public static List<DriverPackage> Parse(string output)
    {
        var result = new List<DriverPackage>();
        var block = new List<string>();

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                Flush(block, result);
                continue;
            }
            // 空行が無い出力でも、公開名を持つ行が来たら直前のレコードを確定する
            if (block.Count >= 2 && PublishedPattern().IsMatch(ValueOf(trimmed)))
            {
                Flush(block, result);
            }
            block.Add(trimmed);
        }
        Flush(block, result);
        return result;
    }

    private static void Flush(List<string> block, List<DriverPackage> result)
    {
        if (block.Count == 0) return;
        var values = block.Select(ValueOf).ToList();
        block.Clear();

        var idx = values.FindIndex(v => PublishedPattern().IsMatch(v));
        if (idx < 0) return;

        // 公開名の次から 元の名前 / プロバイダー名 / クラス名 の順に並ぶ
        string At(int offset) => idx + offset < values.Count ? values[idx + offset] : "";

        var guid = values.Select(v => GuidPattern().Match(v)).FirstOrDefault(m => m.Success)?.Value ?? "";
        DateTime? date = null;
        Version? version = null;
        foreach (var v in values)
        {
            var m = VersionPattern().Match(v);
            if (!m.Success) continue;
            if (DateTime.TryParse(m.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                date = d;
            version = Version.TryParse(m.Groups[2].Value, out var parsed) ? parsed : null;
            break;
        }

        result.Add(new DriverPackage(values[idx], At(1), At(2), At(3), guid, date, version));
    }

    /// <summary>"項目名: 値" の値部分を返す。コロンが無ければ行全体。</summary>
    private static string ValueOf(string line)
    {
        var i = line.IndexOf(':');
        return i < 0 ? line.Trim() : line[(i + 1)..].Trim();
    }
}
