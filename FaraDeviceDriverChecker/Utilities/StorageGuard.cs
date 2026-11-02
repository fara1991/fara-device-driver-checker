namespace FaraDeviceDriverChecker.Utilities;

using System.Text.RegularExpressions;

/// <summary>
/// ストレージ系ドライバー・サービスを判定する。
/// 削除するとブートできなくなるおそれがあるため、該当するものは削除対象として提案しない。
/// </summary>
public static class StorageGuard
{
    // ディスクドライブ / SCSIアダプター / IDE ATAコントローラー / ボリューム / ストレージ ボリューム
    private static readonly HashSet<string> StorageClassGuids = new(StringComparer.OrdinalIgnoreCase)
    {
        "{4d36e967-e325-11ce-bfc1-08002be10318}",
        "{4d36e97b-e325-11ce-bfc1-08002be10318}",
        "{4d36e96a-e325-11ce-bfc1-08002be10318}",
        "{71a27cdd-812a-11d0-bec7-08002be2092f}",
        "{533c5b84-ec70-11d2-9505-00c04f79deaf}"
    };

    private static readonly Regex StorageNamePattern = new(
        @"^(iastor|iaahci|storahci|stornvme|nvme|stor|vmd|msahci|amdsata|amdxata|amdraid|amd_sata|" +
        @"intelide|pciide|atapi|disk|partmgr|volmgr|volsnap|ntfs|refs|fvevol|mpio|megasas|megasr|percsas|hpsa)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>INF名・サービス名・ドライバー名がストレージ系ならtrue。</summary>
    public static bool IsStorageName(string? name)
        => !string.IsNullOrWhiteSpace(name) && StorageNamePattern.IsMatch(name.Trim());

    public static bool IsStorageClass(string? classGuid)
        => !string.IsNullOrWhiteSpace(classGuid) && StorageClassGuids.Contains(classGuid.Trim());
}
