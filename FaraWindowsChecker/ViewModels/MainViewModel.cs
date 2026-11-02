namespace FaraWindowsChecker.ViewModels;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using Models;
using Services;

public class MainViewModel : INotifyPropertyChanged
{
    private bool _isLoading;
    private bool _hasScanned;
    private string _statusMessage = "デバイスカテゴリを選択してスキャンしてください";

    public MainViewModel()
    {
        ScanCommand = new RelayCommand(async () => await ScanDevicesAsync(), () => IsNotLoading && HasSelectedCategories);
        RunUpdateCommand = new RelayCommand(async () => await RunUpdateAsync(), () => IsNotLoading && HasScanned);
        OpenSettingsCommand = new RelayCommand(OpenWindowsUpdateSettings);
        ClearFilterCommand = new RelayCommand(ClearFilter);
        ScanLeftoverCommand = new RelayCommand(async () => await ScanLeftoverAsync(), () => IsNotLoading);
        ExportCommand = new RelayCommand(ExportReport, () => IsNotLoading && (HasScanned || _leftoverResult != null));
        CopyCommandTextCommand = new RelayCommand(CopySuggestedCommand, () => SelectedLeftover is { IsProtected: false, SuggestedCommand.Length: > 0 });
        RestartAsAdminCommand = new RelayCommand(RestartAsAdmin, () => IsNotLoading && !IsAdministrator);
    }

    public ObservableCollection<DeviceCategory> Categories { get; } = new(DeviceCategory.GetAll());
    public ObservableCollection<DeviceInfo> Devices { get; } = [];

    public bool HasSelectedCategories => Categories.Any(c => c.IsSelected);

    public bool IsAllSelected
    {
        get => Categories.All(c => c.IsSelected);
        set
        {
            foreach (var category in Categories)
            {
                category.IsSelected = value;
            }
            OnPropertyChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            _isLoading = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNotLoading));
        }
    }

    public bool IsNotLoading => !IsLoading;

    public bool HasScanned
    {
        get => _hasScanned;
        private set
        {
            _hasScanned = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public ICommand ScanCommand { get; }
    public ICommand RunUpdateCommand { get; }
    public ICommand OpenSettingsCommand { get; }

    // ---- 残骸チェック（読み取り専用。削除などは実行せず、案内コマンドの表示まで） ----

    private readonly LeftoverScanner _leftoverScanner = new();
    private LeftoverScanResult? _leftoverResult;
    private LeftoverItem? _selectedLeftover;

    public ObservableCollection<LeftoverItem> LeftoverItems { get; } = [];

    // ---- 一覧のフィルター（重要度・分類・キーワード） ----

    private const string FilterAll = "すべて";
    private ICollectionView? _leftoverView;
    private string _filterText = "";
    private string _filterSeverity = FilterAll;
    private string _filterCategory = FilterAll;

    public ICollectionView LeftoverView => _leftoverView ??= CreateLeftoverView();

    public string[] SeverityOptions { get; } =
    [
        FilterAll,
        LeftoverItem.GetSeverityDisplay(LeftoverSeverity.Review),
        LeftoverItem.GetSeverityDisplay(LeftoverSeverity.Caution),
        LeftoverItem.GetSeverityDisplay(LeftoverSeverity.Info)
    ];
    public ObservableCollection<string> CategoryOptions { get; } = [FilterAll];

    public string FilterText
    {
        get => _filterText;
        set { _filterText = value; OnFilterChanged(); }
    }

    public string FilterSeverity
    {
        get => _filterSeverity;
        set { _filterSeverity = value; OnFilterChanged(); }
    }

    public string FilterCategory
    {
        get => _filterCategory;
        set { _filterCategory = value ?? FilterAll; OnFilterChanged(); }
    }

    public string FilterSummary => $"表示 {LeftoverView.Cast<object>().Count()} / 全 {LeftoverItems.Count}件";

    public ICommand ClearFilterCommand { get; }

    private void ClearFilter()
    {
        _filterText = "";
        _filterSeverity = FilterAll;
        _filterCategory = FilterAll;
        OnPropertyChanged(nameof(FilterText));
        OnPropertyChanged(nameof(FilterSeverity));
        OnPropertyChanged(nameof(FilterCategory));
        OnFilterChanged();
    }

    private ICollectionView CreateLeftoverView()
    {
        var view = CollectionViewSource.GetDefaultView(LeftoverItems);
        view.Filter = o => o is LeftoverItem item && MatchesFilter(item);
        return view;
    }

    private bool MatchesFilter(LeftoverItem item)
    {
        if (_filterSeverity != FilterAll && item.SeverityDisplay != _filterSeverity) return false;
        if (_filterCategory != FilterAll && item.Category != _filterCategory) return false;
        if (string.IsNullOrWhiteSpace(_filterText)) return true;

        return _filterText.Split([' ', '　'], StringSplitOptions.RemoveEmptyEntries).All(word =>
            item.Title.Contains(word, StringComparison.OrdinalIgnoreCase) ||
            item.Target.Contains(word, StringComparison.OrdinalIgnoreCase) ||
            item.Detail.Contains(word, StringComparison.OrdinalIgnoreCase) ||
            item.Category.Contains(word, StringComparison.OrdinalIgnoreCase) ||
            item.ActionSummary.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    private void OnFilterChanged()
    {
        LeftoverView.Refresh();
        OnPropertyChanged(nameof(FilterSummary));
        OnPropertyChanged(nameof(IsLeftoverListEmpty));
        OnPropertyChanged(nameof(LeftoverEmptyMessage));
    }

    public bool IsLeftoverListEmpty => !LeftoverView.Cast<object>().Any();

    public string LeftoverEmptyMessage => _leftoverResult == null
        ? "「残骸をチェック」を押すと、検出結果がここに表示されます。"
        : "条件に合う項目はありません。";

    public LeftoverItem? SelectedLeftover
    {
        get => _selectedLeftover;
        set
        {
            _selectedLeftover = value;
            OnPropertyChanged();
        }
    }

    public bool IsAdministrator { get; } = LeftoverScanner.IsAdministrator();

    public string AdminStatus => IsAdministrator
        ? "管理者権限: あり（このアプリは検出のみで変更は行いません）"
        : "管理者権限: なし（一部の登録は確認できません。必要なら管理者として起動し直してください）";

    public ICommand ScanLeftoverCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand CopyCommandTextCommand { get; }
    public ICommand RestartAsAdminCommand { get; }

    private async Task ScanLeftoverAsync()
    {
        IsLoading = true;
        StatusMessage = "アンインストール漏れ（残骸）をチェック中…";
        LeftoverItems.Clear();
        SelectedLeftover = null;

        try
        {
            var result = await Task.Run(() => _leftoverScanner.Scan());
            _leftoverResult = result;
            foreach (var item in result.Items) LeftoverItems.Add(item);

            var selectedCategory = _filterCategory;
            // 先頭の「すべて」は残す（全削除すると、コンボボックスの選択が空になって戻らない）
            while (CategoryOptions.Count > 1) CategoryOptions.RemoveAt(CategoryOptions.Count - 1);
            foreach (var category in result.Items.Select(i => i.Category).Distinct().Order()) CategoryOptions.Add(category);
            _filterCategory = CategoryOptions.Contains(selectedCategory) ? selectedCategory : FilterAll;
            OnPropertyChanged(nameof(FilterCategory));
            OnFilterChanged();

            var review = result.Items.Count(i => i.Severity == LeftoverSeverity.Review);
            StatusMessage = $"残骸チェック完了: {result.Items.Count}件（要確認 {review}件）";
        }
        catch (Exception ex)
        {
            StatusMessage = $"残骸チェックに失敗しました（詳細: {ex.Message}）";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ExportReport()
    {
        var dialog = new SaveFileDialog
        {
            Title = "診断結果のエクスポート",
            Filter = "テキスト (*.txt)|*.txt|JSON (*.json)|*.json",
            FileName = $"FaraWindowsChecker-{DateTime.Now:yyyyMMdd-HHmmss}"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var devices = Devices.ToList();
            var content = dialog.FilterIndex == 2 || dialog.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? ReportExporter.ToJson(devices, _leftoverResult)
                : ReportExporter.ToText(devices, _leftoverResult);
            File.WriteAllText(dialog.FileName, content, new System.Text.UTF8Encoding(true));
            StatusMessage = $"エクスポートしました: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"ファイルを保存できませんでした。保存先と書き込み権限を確認してください。\r\n\r\n詳細: {ex.Message}",
                "エクスポートに失敗しました", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CopySuggestedCommand()
    {
        if (SelectedLeftover is not { IsProtected: false } item || item.SuggestedCommand == "") return;
        try
        {
            Clipboard.SetText(item.SuggestedCommand);
            StatusMessage = "コマンドをクリップボードにコピーしました（自動では実行されません。内容を確認して手動で実行してください）";
        }
        catch (COMException ex)
        {
            // 他のアプリがクリップボードを使用中のときなど
            StatusMessage = $"クリップボードにコピーできませんでした。少し待ってから、もう一度お試しください（詳細: {ex.Message}）";
        }
    }

    private void RestartAsAdmin()
    {
        var confirm = MessageBox.Show(
            "このアプリを管理者権限で起動し直します。現在の結果は破棄されます。よろしいですか？",
            "管理者として再起動", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Environment.ProcessPath!,
                UseShellExecute = true,
                Verb = "runas"
            });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            // UACのキャンセルなど
            StatusMessage = $"管理者として再起動しませんでした（詳細: {ex.Message}）";
        }
    }

    private async Task ScanDevicesAsync()
    {
        var selectedCategories = Categories.Where(c => c.IsSelected).ToList();
        if (selectedCategories.Count == 0) return;

        IsLoading = true;
        Devices.Clear();

        try
        {
            var allClasses = selectedCategories.SelectMany(c => c.Classes).ToArray();
            var categoryNames = string.Join("、", selectedCategories.Select(c => c.Name));
            StatusMessage = $"{categoryNames}をスキャン中…";

            var result = await Task.Run(() => DeviceService.GetDevices(allClasses));
            var devices = result.Devices;

            foreach (var device in devices.OrderByDescending(d => d.HasProblem))
            {
                Devices.Add(device);
            }

            var problemCount = devices.Count(d => d.HasProblem);
            StatusMessage = $"{devices.Count}件のデバイス / 問題: {problemCount}件" +
                            (result.FailedCount > 0 ? $"（一覧を取得できなかったクラス: {result.FailedCount}件）" : "");
            HasScanned = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"スキャンに失敗しました（詳細: {ex.Message}）";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OpenWindowsUpdateSettings()
    {
        if (!DeviceService.OpenWindowsUpdateSettings())
        {
            StatusMessage = "Windows Updateの設定を開けませんでした";
        }
    }

    private async Task RunUpdateAsync()
    {
        IsLoading = true;
        StatusMessage = "ドライバーを更新中…";

        try
        {
            var (success, message) = await DeviceService.RunWindowsUpdateAsync();
            StatusMessage = message;

            if (success)
            {
                MessageBox.Show("ドライバーの更新が完了しました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"ドライバーの更新に失敗しました（詳細: {ex.Message}）";
            MessageBox.Show($"ドライバーの更新中にエラーが発生しました。\r\n\r\n詳細: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
