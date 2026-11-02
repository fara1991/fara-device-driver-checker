namespace FaraWindowsChecker.Utilities;

using System.Runtime.InteropServices;
using System.Text;

/// <summary>キャッシュ済みMSI（C:\Windows\Installer\*.msi）を読み取り専用で開き、テーブルを問い合わせる。</summary>
public sealed class MsiDatabase : IDisposable
{
    private const int MsiDbOpenReadOnly = 0;
    private const int ErrorNoMoreItems = 259;
    private const int ErrorMoreData = 234;

    private IntPtr _handle;

    private MsiDatabase(IntPtr handle) => _handle = handle;

    /// <summary>開けなければnullを返す（破損・アクセス拒否など）。</summary>
    public static MsiDatabase? TryOpen(string path)
    {
        try
        {
            // persistに0 (MSIDBOPEN_READONLY) を渡す。文字列ポインター版ではなく整数版を使う
            return MsiOpenDatabaseW(path, new IntPtr(MsiDbOpenReadOnly), out var h) == 0 ? new MsiDatabase(h) : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>1列目の文字列を返すクエリを実行する。テーブルが無い場合は空のリストを返す。</summary>
    public List<string> QueryFirstColumn(string sql)
    {
        var rows = new List<string>();
        if (_handle == IntPtr.Zero) return rows;
        if (MsiDatabaseOpenViewW(_handle, sql, out var view) != 0) return rows;

        try
        {
            if (MsiViewExecute(view, IntPtr.Zero) != 0) return rows;
            while (MsiViewFetch(view, out var record) != ErrorNoMoreItems)
            {
                if (record == IntPtr.Zero) break;
                try
                {
                    var size = 512;
                    var sb = new StringBuilder(size);
                    var result = MsiRecordGetStringW(record, 1, sb, ref size);
                    if (result == ErrorMoreData)
                    {
                        // バッファーが足りないときは、size に必要な文字数が入って返る。大きくして読み直す
                        sb = new StringBuilder(size + 1);
                        size = sb.Capacity;
                        result = MsiRecordGetStringW(record, 1, sb, ref size);
                    }
                    if (result == 0)
                        rows.Add(sb.ToString());
                }
                finally
                {
                    MsiCloseHandle(record);
                }
            }
        }
        finally
        {
            MsiCloseHandle(view);
        }
        return rows;
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        MsiCloseHandle(_handle);
        _handle = IntPtr.Zero;
    }

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiOpenDatabaseW(string path, IntPtr persist, out IntPtr database);

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiDatabaseOpenViewW(IntPtr database, string query, out IntPtr view);

    [DllImport("msi.dll")]
    private static extern uint MsiViewExecute(IntPtr view, IntPtr record);

    [DllImport("msi.dll")]
    private static extern uint MsiViewFetch(IntPtr view, out IntPtr record);

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiRecordGetStringW(IntPtr record, uint field, StringBuilder value, ref int size);

    [DllImport("msi.dll")]
    private static extern uint MsiCloseHandle(IntPtr handle);
}
