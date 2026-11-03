namespace FaraWindowsChecker.Utilities;

using System.Globalization;

/// <summary>
/// 案内コマンドに埋め込む値（レジストリのキー名、フォルダー名など）が、引用符の外へ抜け出せないかを確認する。
/// 安全でない値のときは、案内コマンドを表示しない側に倒す。
/// </summary>
public static class CommandGuard
{
    /// <summary>PowerShellは、見た目の似た引用符（“ ” ‘ ’ など）も引用符として扱う。</summary>
    private const string DoubleQuoteChars = "\"\u201C\u201D\u201E";
    private const string SingleQuoteChars = "'\u2018\u2019\u201A\u201B";

    /// <summary>二重引用符 "…" の中に埋め込んでよい値か（cmd・PowerShell共通）。</summary>
    public static bool IsSafeInDoubleQuotes(string? value)
        => IsPlain(value) && value!.IndexOfAny(DoubleQuoteUnsafe) < 0;

    /// <summary>単一引用符 '…' の中（PowerShell）に埋め込んでよい値か。</summary>
    public static bool IsSafeInSingleQuotes(string? value)
        => IsPlain(value) && value!.IndexOfAny(SingleQuoteUnsafe) < 0;

    private static readonly char[] DoubleQuoteUnsafe = (DoubleQuoteChars + "$`%").ToCharArray();
    private static readonly char[] SingleQuoteUnsafe = SingleQuoteChars.ToCharArray();

    private static bool IsPlain(string? value)
        => !string.IsNullOrEmpty(value) && !value.Any(IsHiddenOrControl);

    /// <summary>制御文字と、表示と実行内容をずらせる不可視の文字（Bidi制御、ゼロ幅、行・段落の区切り）。</summary>
    private static bool IsHiddenOrControl(char c)
        => char.GetUnicodeCategory(c) is UnicodeCategory.Control or UnicodeCategory.Format
            or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
}
