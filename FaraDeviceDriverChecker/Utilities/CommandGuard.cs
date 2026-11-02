namespace FaraDeviceDriverChecker.Utilities;

/// <summary>
/// 案内コマンドに埋め込む値（レジストリのキー名、フォルダー名など）が、引用符の外へ抜け出せないかを確認する。
/// 安全でない値のときは、案内コマンドを表示しない側に倒す。
/// </summary>
public static class CommandGuard
{
    /// <summary>PowerShell は、見た目の似た引用符（“ ” ‘ ’ など）も引用符として扱う。</summary>
    private const string DoubleQuoteChars = "\"“”„";
    private const string SingleQuoteChars = "'‘’‚‛";

    /// <summary>二重引用符 "…" の中に埋め込んでよい値か（cmd・PowerShell 共通）。</summary>
    public static bool IsSafeInDoubleQuotes(string? value)
        => IsPlain(value) && value!.IndexOfAny((DoubleQuoteChars + "$`%").ToCharArray()) < 0;

    /// <summary>単一引用符 '…' の中（PowerShell）に埋め込んでよい値か。</summary>
    public static bool IsSafeInSingleQuotes(string? value)
        => IsPlain(value) && value!.IndexOfAny(SingleQuoteChars.ToCharArray()) < 0;

    private static bool IsPlain(string? value)
        => !string.IsNullOrEmpty(value) && !value.Any(char.IsControl);
}
