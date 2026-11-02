namespace FaraDeviceDriverChecker.Services;

using System.Globalization;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using Models;
using Utilities;
using static FaraDeviceDriverChecker.Utilities.CommandBuilder;

// 残骸チェック: フォルダー（Package Cache・Program Files）
public partial class LeftoverScanner
{
    // ---- フォルダー ----

    private void CheckPackageCache(List<UninstallEntry> entries)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Package Cache");
        if (!Directory.Exists(root)) return;

        using var hklm = OpenHklm64();
        var registered = entries.Select(e => e.KeyName.ToUpperInvariant()).ToHashSet();

        foreach (var dir in Directory.GetDirectories(root))
        {
            var m = Regex.Match(Path.GetFileName(dir), @"^(\{[0-9A-Fa-f-]{36}\})");
            if (!m.Success) continue;

            var guid = m.Groups[1].Value.ToUpperInvariant();
            if (registered.Contains(guid)) continue;
            if (entries.Any(e => e.BundleCachePath.Contains(guid, StringComparison.OrdinalIgnoreCase) ||
                                 e.UninstallString.Contains(guid, StringComparison.OrdinalIgnoreCase))) continue;

            var packed = MsiGuid.Pack(guid);
            using var prod = packed == null ? null : hklm.OpenSubKey($@"{InstallerProductsPath}\{packed}");
            using var dep = hklm.OpenSubKey($@"{InstallerDependenciesPath}\{guid}");
            if (prod != null || dep != null) continue;

            Add(CatFolder, LeftoverSeverity.Caution,
                $"登録の無いPackage Cache: {Path.GetFileName(dir)}", dir,
                $"Uninstall・Installer\\Products・Dependenciesのいずれにも {guid}が見つかりません。" +
                $"サイズ: {FormatSize(DirectorySize(dir))}",
                RemoveFolderCommand(dir, "-Recurse -Force"));
        }
    }

    private void CheckProgramFolders(List<UninstallEntry> entries)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(r => !string.IsNullOrEmpty(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        using var hklm = OpenHklm64();
        var productNames = new List<string>(entries.Select(e => e.DisplayName));
        using (var products = hklm.OpenSubKey(InstallerProductsPath))
        {
            foreach (var packed in products?.GetSubKeyNames() ?? [])
            {
                using var p = products!.OpenSubKey(packed);
                if (p != null) productNames.Add(ReadString(p, RegValue.ProductName) ?? "");
            }
        }

        foreach (var root in roots)
        {
            foreach (var (relative, keyword) in KnownProductFolders)
            {
                var folder = Path.Combine(root, relative);
                if (!Directory.Exists(folder)) continue;
                if (productNames.Any(n => n.Contains(keyword, StringComparison.OrdinalIgnoreCase))) continue;

                Add(CatFolder, LeftoverSeverity.Review,
                    $"登録の無い製品フォルダー: {relative}", folder,
                    $"「{keyword}」を含む製品がUninstall / Installer\\Productsに登録されていません。" +
                    "アンインストール後に残ったフォルダーの可能性があります。",
                    RemoveFolderCommand(folder, "-Recurse -Force"));
            }

            // 中身がファイルを1つも持たない空のフォルダー（ベンダー直下）
            foreach (var vendor in SafeDirectories(root))
            {
                if (SafeHasAnyFile(vendor)) continue;
                Add(CatFolder, LeftoverSeverity.Info,
                    $"ファイルの無い空のフォルダー: {Path.GetFileName(vendor)}", vendor,
                    "配下にファイルが1つもありません。",
                    RemoveFolderCommand(vendor, "-Recurse"));
            }
        }
    }

    private IEnumerable<string> SafeDirectories(string path)
    {
        try
        {
            return Directory.GetDirectories(path);
        }
        catch (Exception ex) when (IsAccessProblem(ex))
        {
            _unreadablePaths++;
            return [];
        }
    }

    private bool SafeHasAnyFile(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Any();
        }
        catch (Exception ex) when (IsAccessProblem(ex))
        {
            _unreadablePaths++;
            return true; // 読めないフォルダーは空と判断しない
        }
    }

    private long DirectorySize(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        }
        catch (Exception ex) when (IsAccessProblem(ex))
        {
            _unreadablePaths++;
            return 0;
        }
    }

    private static string FormatSize(long bytes)
        => bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0:F1} MB" : $"{bytes / 1024.0:F1} KB";
}
