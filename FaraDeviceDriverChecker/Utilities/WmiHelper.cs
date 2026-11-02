namespace FaraDeviceDriverChecker.Utilities;

using System.Management;

public static class WmiHelper
{
    /// <summary>
    /// WMI のプロパティを文字列で返す。値が無いときは「不明」、読み取れないときは「取得不可」。
    /// 配列（HardwareID など）は「 / 」で連結する。
    /// </summary>
    public static string GetPropertyValue(ManagementBaseObject obj, string propertyName)
    {
        try
        {
            return obj[propertyName] switch
            {
                null => "不明",
                Array array => string.Join(" / ", array.Cast<object?>().Select(v => v?.ToString())),
                var value => value.ToString() ?? "不明"
            };
        }
        catch (ManagementException)
        {
            return "取得不可";
        }
    }
}
