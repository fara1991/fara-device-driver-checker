using FaraWindowsChecker.ViewModels;
using Xunit;

namespace FaraWindowsChecker.Tests;

/// <summary>メニューバーから使うコマンド（タブの切り替え、再スキャン）の確認。</summary>
public class MainViewModelMenuTests
{
    [Fact]
    public void最初は_ドライバー診断のタブが選ばれている()
        => Assert.Equal(MainViewModel.DriversTabIndex, new MainViewModel().SelectedTabIndex);

    [Fact]
    public voidタブを切り替えるコマンドで_表示中のタブが変わり_変更が通知される()
    {
        var viewModel = new MainViewModel();
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.ShowLeftoverTabCommand.Execute(null);
        Assert.Equal(MainViewModel.LeftoverTabIndex, viewModel.SelectedTabIndex);

        viewModel.ShowDriversTabCommand.Execute(null);
        Assert.Equal(MainViewModel.DriversTabIndex, viewModel.SelectedTabIndex);

        Assert.Equal(2, changed.Count(n => n == nameof(MainViewModel.SelectedTabIndex)));
    }

    [Fact]
    public void同じタブを選び直しても_通知しない()
    {
        var viewModel = new MainViewModel();
        var notified = false;
        viewModel.PropertyChanged += (_, _) => notified = true;

        viewModel.SelectedTabIndex = MainViewModel.DriversTabIndex;

        Assert.False(notified);
    }

    [Fact]
    public void再スキャンは_表示中のタブに応じて_実行できるかが変わる()
    {
        var viewModel = new MainViewModel();

        // ドライバー診断: カテゴリを選んでいないため、スキャンできない
        viewModel.SelectedTabIndex = MainViewModel.DriversTabIndex;
        Assert.False(viewModel.RescanCommand.CanExecute(null));

        // 残骸チェック: 選ぶものが無く、いつでも実行できる
        viewModel.SelectedTabIndex = MainViewModel.LeftoverTabIndex;
        Assert.True(viewModel.RescanCommand.CanExecute(null));

        // ドライバー診断に戻して、カテゴリを選ぶと、実行できる
        viewModel.SelectedTabIndex = MainViewModel.DriversTabIndex;
        viewModel.Categories[0].IsSelected = true;
        Assert.True(viewModel.RescanCommand.CanExecute(null));
    }

    [Fact]
    public voidスキャンの前は_エクスポートと_ドライバーの更新は実行できない()
    {
        var viewModel = new MainViewModel();

        Assert.False(viewModel.ExportCommand.CanExecute(null));
        Assert.False(viewModel.RunUpdateCommand.CanExecute(null));
        Assert.False(viewModel.CopyCommandTextCommand.CanExecute(null));
        Assert.True(viewModel.OpenSettingsCommand.CanExecute(null));
    }
}
