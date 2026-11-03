namespace FaraWindowsChecker.Models;

using System.ComponentModel;
using System.Runtime.CompilerServices;

public class DeviceCategory : INotifyPropertyChanged
{
    private bool _isSelected;

    public required string Name { get; set; }
    public required string[] Classes { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public static List<DeviceCategory> GetAll() =>
    [
        new() { Name = "オーディオ", Classes = ["Media", "AudioEndpoint"] },
        new() { Name = "ディスプレイ", Classes = ["Display"] },
        new() { Name = "ネットワーク", Classes = ["Net"] },
        new() { Name = "Bluetooth", Classes = ["Bluetooth"] },
        new() { Name = "USB", Classes = ["USB"] },
        new() { Name = "キーボード・マウス", Classes = ["Keyboard", "Mouse"] },
        new() { Name = "カメラ", Classes = ["Camera"] }
    ];

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
