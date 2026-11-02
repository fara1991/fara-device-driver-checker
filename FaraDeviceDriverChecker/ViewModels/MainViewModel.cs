namespace FaraDeviceDriverChecker.ViewModels;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Models;
using Services;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly DeviceService _deviceService = new();
    private bool _isLoading;
    private bool _hasScanned;
    private string _statusMessage = "デバイスカテゴリを選択してスキャンしてください";

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

    public ICommand ScanCommand => new RelayCommand(async () => await ScanDevicesAsync(), () => IsNotLoading && HasSelectedCategories);
    public ICommand RunUpdateCommand => new RelayCommand(async () => await RunUpdateAsync(), () => IsNotLoading && HasScanned);
    public ICommand OpenSettingsCommand => new RelayCommand(() => _deviceService.OpenWindowsUpdateSettings());

    // ---- 残骸チェック（読み取り専用。削除などは実行せず、案内コマンドの表示まで） ----

    private readonly LeftoverScanner _leftoverScanner = new();
    private LeftoverScanResult? _leftoverResult;
    private LeftoverItem? _selectedLeftover;

    public ObservableCollection<LeftoverItem> LeftoverItems { get; } = [];

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

    public ICommand ScanLeftoverCommand => new RelayCommand(async () => await ScanLeftoverAsync(), () => IsNotLoading);
    public ICommand ExportCommand => new RelayCommand(ExportReport, () => IsNotLoading && (HasScanned || _leftoverResult != null));
    public ICommand CopyCommandTextCommand => new RelayCommand(CopySuggestedCommand, () => SelectedLeftover is { IsProtected: false, SuggestedCommand.Length: > 0 });
    public ICommand RestartAsAdminCommand => new RelayCommand(RestartAsAdmin, () => IsNotLoading && !IsAdministrator);

    private async Task ScanLeftoverAsync()
    {
        IsLoading = true;
        StatusMessage = "アンインストール漏れ（残骸）をチェック中...";
        LeftoverItems.Clear();
        SelectedLeftover = null;

        try
        {
            var result = await Task.Run(() => _leftoverScanner.Scan());
            _leftoverResult = result;
            foreach (var item in result.Items) LeftoverItems.Add(item);

            var review = result.Items.Count(i => i.Severity == LeftoverSeverity.Review);
            StatusMessage = $"残骸チェック完了: {result.Items.Count}件（要確認 {review}件）";
        }
        catch (Exception ex)
        {
            StatusMessage = $"エラー: {ex.Message}";
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
            FileName = $"FaraDeviceDriverChecker-{DateTime.Now:yyyyMMdd-HHmmss}"
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
            MessageBox.Show(ex.Message, "エクスポートに失敗しました", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CopySuggestedCommand()
    {
        if (SelectedLeftover is not { IsProtected: false } item || item.SuggestedCommand == "") return;
        Clipboard.SetText(item.SuggestedCommand);
        StatusMessage = "コマンドをクリップボードにコピーしました（自動では実行されません。内容を確認して手動で実行してください）";
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
            // UAC のキャンセルなど
            StatusMessage = $"再起動しませんでした: {ex.Message}";
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
            var categoryNames = string.Join(", ", selectedCategories.Select(c => c.Name));
            StatusMessage = $"{categoryNames}をスキャン中...";

            var devices = await Task.Run(() => _deviceService.GetDevices(allClasses));

            foreach (var device in devices)
            {
                Devices.Add(device);
            }

            var problemCount = devices.Count(d => d.HasProblem);
            StatusMessage = $"{devices.Count}個のデバイス / 問題: {problemCount}個";
            HasScanned = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"エラー: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RunUpdateAsync()
    {
        IsLoading = true;
        StatusMessage = "Updateを実行中...";

        try
        {
            var (success, message) = await _deviceService.RunWindowsUpdateAsync();
            StatusMessage = message;

            if (success)
            {
                MessageBox.Show("Updateが完了しました", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"エラー: {ex.Message}";
            MessageBox.Show(ex.Message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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

public class RelayCommand : ICommand
{
    private readonly Action? _execute;
    private readonly Func<Task>? _executeAsync;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public RelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
    {
        _executeAsync = executeAsync;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public async void Execute(object? parameter)
    {
        if (_executeAsync != null)
        {
            await _executeAsync();
        }
        else
        {
            _execute?.Invoke();
        }
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
