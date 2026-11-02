using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using FaraWindowsChecker.ViewModels;

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

    /// <summary>「キーワードで絞り込み」(Ctrl+F): 残骸チェックのタブへ移り、キーワードの入力欄にカーソルを置く。</summary>
    private void FindExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.SelectedTabIndex = MainViewModel.LeftoverTabIndex;
        }

        // タブの切り替えが画面に反映されてから、カーソルを置く
        Dispatcher.BeginInvoke(() =>
        {
            FilterTextBox.Focus();
            FilterTextBox.SelectAll();
        }, DispatcherPriority.Loaded);
    }
}
