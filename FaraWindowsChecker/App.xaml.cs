using System.Windows;

namespace FaraWindowsChecker;

/// <summary>アプリケーションの起点。</summary>
public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    /// <summary>
    /// 想定していない例外で、メッセージも出さずにアプリが終了しないようにする。
    /// 内容を表示して、そのまま使い続けられるようにする。
    /// </summary>
    private static void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"予期しないエラーが発生しました。操作をやり直してください。\r\n\r\n詳細: {e.Exception.Message}",
            "予期しないエラー", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
