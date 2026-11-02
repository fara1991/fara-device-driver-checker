namespace FaraWindowsChecker.Models;

/// <summary>残骸チェックの重要度。</summary>
public enum LeftoverSeverity
{
    /// <summary>参考情報（アクセス不可など）。</summary>
    Info,
    /// <summary>残骸の可能性があるが、影響は小さい。</summary>
    Caution,
    /// <summary>アンインストール失敗などの原因になり得るため要確認。</summary>
    Review
}

/// <summary>残骸チェックで検出した1件分の結果。</summary>
public class LeftoverItem
{
    public required string Category { get; init; }
    public required LeftoverSeverity Severity { get; init; }
    public required string Title { get; init; }

    /// <summary>対象（レジストリキー、パス、サービス名など）。</summary>
    public required string Target { get; init; }

    public string Detail { get; init; } = "";

    /// <summary>影響と対応の案内。</summary>
    public LeftoverGuidance? Guidance { get; init; }

    /// <summary>対応の目安（一覧に表示する短い言葉）。</summary>
    public string ActionSummary => Guidance?.Summary ?? "";

    /// <summary>放置した場合の影響。</summary>
    public string Impact => Guidance?.Impact ?? "";

    /// <summary>推奨する対応。</summary>
    public string Action => Guidance?.Action ?? "";

    /// <summary>手動で実行する場合の案内コマンド。ストレージ系などの保護対象では空にする。</summary>
    public string SuggestedCommand { get; init; } = "";

    /// <summary>ストレージ系など、削除対象として提案しない項目。</summary>
    public bool IsProtected { get; init; }

    public string SeverityDisplay => GetSeverityDisplay(Severity);

    /// <summary>重要度の表示名。画面・フィルターの選択肢・エクスポートで共通に使う。</summary>
    public static string GetSeverityDisplay(LeftoverSeverity severity) => severity switch
    {
        LeftoverSeverity.Review => "要確認",
        LeftoverSeverity.Caution => "注意",
        _ => "情報"
    };

    public string CommandDisplay => IsProtected
        ? "（ストレージ系のため削除対象として提案しません）"
        : SuggestedCommand;
}
