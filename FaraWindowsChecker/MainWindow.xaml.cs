using System.Windows;

namespace FaraWindowsChecker;

/// <summary>メインウィンドウ。</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // 小さい画面でも、ウィンドウが作業領域（タスクバーを除く領域）からはみ出さないようにする
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(Width, workArea.Width);
        Height = Math.Min(Height, workArea.Height);
    }
}
