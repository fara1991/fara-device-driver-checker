namespace FaraWindowsChecker.Utilities;

using System.Text;
using System.Text.RegularExpressions;

/// <summary>Windows Installerの「圧縮GUID」（Installer\Productsのキー名）と通常GUIDの相互変換。</summary>
public static class MsiGuid
{
    private static readonly Regex GuidPattern =
        new(@"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$");

    public static bool IsGuid(string text) => GuidPattern.IsMatch(text);

    /// <summary>{409CB30E-...} 形式を32桁の圧縮GUIDに変換する。変換できなければnull。</summary>
    public static string? Pack(string guid)
    {
        if (!IsGuid(guid)) return null;
        var hex = guid.Trim('{', '}').Replace("-", "").ToUpperInvariant();
        var sb = new StringBuilder(32);
        sb.Append(Reverse(hex[..8])).Append(Reverse(hex[8..12])).Append(Reverse(hex[12..16]));
        for (var i = 16; i < 32; i += 2)
        {
            sb.Append(hex[i + 1]).Append(hex[i]);
        }
        return sb.ToString();
    }

    /// <summary>32桁の圧縮GUIDを {XXXXXXXX-...} 形式に戻す。変換できなければnull。</summary>
    public static string? Unpack(string packed)
    {
        if (packed.Length != 32 || !packed.All(Uri.IsHexDigit)) return null;
        var p = packed.ToUpperInvariant();
        var sb = new StringBuilder();
        sb.Append('{').Append(Reverse(p[..8])).Append('-').Append(Reverse(p[8..12])).Append('-')
          .Append(Reverse(p[12..16])).Append('-');
        sb.Append(p[17]).Append(p[16]).Append(p[19]).Append(p[18]).Append('-');
        for (var i = 20; i < 32; i += 2)
        {
            sb.Append(p[i + 1]).Append(p[i]);
        }
        return sb.Append('}').ToString();
    }

    private static string Reverse(string s)
    {
        var chars = s.ToCharArray();
        Array.Reverse(chars);
        return new string(chars);
    }
}
