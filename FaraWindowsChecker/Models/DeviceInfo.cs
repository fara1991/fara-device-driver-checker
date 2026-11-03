namespace FaraWindowsChecker.Models;

using System.ComponentModel;
using System.Runtime.CompilerServices;

public class DeviceInfo : INotifyPropertyChanged
{
    private bool _isSelected;

    public required string Name { get; set; }
    public required string DeviceId { get; set; }
    public required string Description { get; set; }
    public required string Manufacturer { get; set; }
    public required string Status { get; set; }
    public required string Class { get; set; }
    public required string Service { get; set; }
    public string DriverVersion { get; set; } = "不明";
    public string DriverDate { get; set; } = "不明";
    public bool HasProblem { get; set; }
    public string ProblemCode { get; set; } = "正常";
    public string HardwareId { get; set; } = "不明";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public string StatusDisplay => HasProblem ? $"問題あり: {ProblemCode}" : "正常";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
