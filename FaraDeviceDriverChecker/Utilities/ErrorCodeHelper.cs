namespace FaraDeviceDriverChecker.Utilities;

public static class ErrorCodeHelper
{
    private static readonly Dictionary<uint, string> ProblemCodes = new()
    {
        { 0, "正常" },
        { 1, "正しく構成されていません" },
        { 3, "ドライバーが破損している可能性があります" },
        { 10, "デバイスを開始できません" },
        { 12, "このデバイスが使用できる空きリソースが不足しています" },
        { 18, "このデバイスのドライバーを再インストールしてください" },
        { 22, "このデバイスは無効です" },
        { 28, "このデバイスのドライバーがインストールされていません" },
        { 31, "このデバイスは正しく動作していません" },
        { 37, "Windowsでこのデバイス用のドライバーを読み込むことができません" },
        { 39, "ドライバーが破損しているか、ドライバーがありません" },
        { 43, "以前のインスタンスが実行されているため、デバイスは停止されました" },
        { 45, "現在、このハードウェア デバイスはコンピューターに接続されていません" }
    };

    public static string GetProblemDescription(uint problemCode)
    {
        return ProblemCodes.TryGetValue(problemCode, out var description)
            ? description
            : $"不明なエラー ({problemCode})";
    }
}
