namespace FaraWindowsChecker.Models;

/// <summary>
/// 残骸チェックの1件に添える案内。「放置するとどうなるか」と「どう対応すればよいか」を伝える。
/// 「今の状態」は検出時に作る詳細（LeftoverItem.Detail）で伝える。
/// </summary>
/// <param name="Summary">対応の目安（一覧に表示する短い言葉）。</param>
/// <param name="Impact">放置した場合の影響。</param>
/// <param name="Action">推奨する対応。</param>
public record LeftoverGuidance(string Summary, string Impact, string Action)
{
    private const string StorageWarning = "ストレージ系のため、手動で削除・登録解除しないでください（起動できなくなるおそれがあります）。";

    // ---- レジストリ ----

    public static readonly LeftoverGuidance UninstallerMissing = new(
        "登録の削除を検討",
        "「アプリと機能」に項目が残り、アンインストールを実行しても失敗します。アプリ本体は、すでに無い可能性があります。",
        "アプリがすでにアンインストール済みなら、案内コマンドでUninstall情報を削除します（バックアップ付き）。まだ使っているアプリなら、削除せずに、そのアプリを再インストールしてください。");

    public static readonly LeftoverGuidance InstallLocationMissing = new(
        "要確認",
        "多くの場合、実害はありません。アプリの一覧に項目が残る程度です。一部のアプリでは、更新や修復が失敗します。",
        "アプリがすでに不要なら、案内コマンドでUninstall情報を削除します。別の場所へ移動して使っているなら、そのままで問題ありません。");

    public static readonly LeftoverGuidance MsiRegistrationMissing = new(
        "登録の削除を検討",
        "Windows Installerが製品を見つけられず、アンインストールや修復がエラーで失敗します。",
        "同じ製品を再インストールしてから、アンインストールし直すと解消することがあります。解消しない場合や、製品がすでに不要な場合は、案内コマンドでUninstall情報を削除します（バックアップ付き）。");

    public static readonly LeftoverGuidance CachedMsiMissing = new(
        "再インストールで解消",
        "修復やアンインストールが、元のMSIが見つからないというエラーで失敗します。",
        "同じバージョンのインストーラーを入手して、再インストール（修復）してから、アンインストールし直します。製品がすでに不要なら、案内コマンドで登録を削除できます（バックアップ付き）。");

    public static readonly LeftoverGuidance ProductsOnly = new(
        "登録の削除を検討",
        "Windows Installerの製品一覧にだけ残る登録です。同じ製品の再インストールが失敗することがあります。",
        "製品がすでにアンインストール済みなら、案内コマンドで登録を削除します（バックアップ付き）。");

    public static readonly LeftoverGuidance UserDataOnly = new(
        "登録の削除を検討",
        "実害は小さいですが、同じ製品を再インストールするときに、古い情報が干渉することがあります。",
        "製品がすでに無いなら、案内コマンドで登録を削除します（バックアップ付き）。");

    // ---- ドライバー ----

    public static LeftoverGuidance MsiInfNotInStore(bool isStorage) => new(
        isStorage ? "再インストールで解消" : "要確認",
        "アンインストール時のドライバー削除が失敗し（0x490）、MSIがエラー1603で終了します。アンインストールできなくなります。",
        "同じバージョンのインストーラーで再インストール（修復）して、ドライバーを登録し直してから、アンインストールし直します。" +
        (isStorage ? StorageWarning : ""));

    public static LeftoverGuidance DeviceReferencesMissingInf(bool isStorage) => new(
        "ドライバーを入れ直す",
        "デバイスのドライバー情報が壊れており、更新や再インストールができない、または正しく動作しない可能性があります。",
        "デバイスマネージャーで該当のデバイスを開き、「ドライバーの更新」を試してください。直らなければ、メーカーのドライバーを入手して入れ直します。" +
        (isStorage ? "ストレージ系のため、削除はせず、ドライバーの再インストールだけを行ってください。" : ""));

    public static LeftoverGuidance DuplicateDriver(bool used, bool isStorage) => new(
        used || isStorage ? "対応不要" : "削除を検討",
        "古い版のドライバーがドライバーストアに残っています。容量を使いますが、通常は実害はありません。",
        isStorage ? "ストレージ系のため、削除しないでください。"
        : used ? "デバイスが使用中のため、削除しないでください。"
        : "どのデバイスにも使われていないため、案内コマンド（pnputil /delete-driver）で削除できます。念のため、復元ポイントを作ってから実行してください。");

    /// <summary>問題のあるデバイス。エラーコードごとに、影響と対応を案内する。</summary>
    public static LeftoverGuidance ProblemDevice(uint code) => code switch
    {
        0 => new("状態を確認",
            "デバイスの状態が正常ではありません（切断中や省電力など）。デバイスが使えない場合があります。",
            "デバイスマネージャーで状態を確認してください。接続し直すと、解消することがあります。"),
        1 or 10 or 31 or 37 or 39 or 43 => new("ドライバーを入れ直す",
            "デバイスが正しく動作していません。",
            "デバイスマネージャーで「ドライバーの更新」を試してください。直らなければ、デバイスをアンインストールして、PCを再起動します。"),
        3 or 18 => new("ドライバーを入れ直す",
            "ドライバーが壊れている、またはメモリが不足している可能性があります。",
            "PCを再起動してください。直らなければ、ドライバーを再インストールします。"),
        12 => new("要確認",
            "デバイスが使えるリソース（割り込みなど）が不足しています。",
            "同じ接続口に付けている、使っていないデバイスを取り外してください。"),
        22 => new("有効にする",
            "デバイスが無効にされているため、使えません。",
            "意図して無効にしたのでなければ、デバイスマネージャーで「デバイスを有効にする」を選びます。"),
        28 => new("ドライバーを入れる",
            "ドライバーがインストールされていないため、デバイスが使えません。",
            "「Windows Updateの設定を開く」から更新を確認するか、メーカーのドライバーをインストールしてください。"),
        45 => new("対応不要の場合あり",
            "デバイスが、現在は接続されていません。",
            "使っているデバイスなら、接続し直してください。使っていないなら、対応は不要です。"),
        _ => new("状態を確認",
            "デバイスが正しく動作していない可能性があります。",
            "デバイスマネージャーで状態とエラーコードを確認し、ドライバーの更新を試してください。")
    };

    // ---- サービス ----

    public static readonly LeftoverGuidance ServiceMissingExe = new(
        "削除を検討",
        "サービスの開始に失敗し、イベントログにエラーが残ります。一覧に項目が残ります。",
        "関連するアプリがすでにアンインストール済みなら、案内コマンドで、設定を確認（sc qc）してから削除（sc delete）します。まだ使うアプリのサービスなら、そのアプリを再インストールしてください。");

    public static readonly LeftoverGuidance ServiceStoppedDisabled = new(
        "意図を確認",
        "停止・無効のままで、通常は実害はありません。アンインストールの残りの場合があります。",
        "ご自身で無効にしたサービスなら、対応は不要です。関連するソフトがすでに無いなら、案内コマンドで削除できます。");

    public static LeftoverGuidance KernelDriverServiceMissing(bool loadedAtBoot, bool isStorage) => new(
        isStorage ? "対応不要" : "削除を検討",
        loadedAtBoot
            ? "起動時に読み込まれる設定のため、起動時のエラーや、起動の遅れの原因になることがあります。"
            : "手動で開始する設定のため、通常は実害はありません。一覧に残るだけです。",
        isStorage
            ? StorageWarning
            : "関連するソフトがすでにアンインストール済みなら、案内コマンドで、レジストリの登録を削除します（バックアップ付き）。");

    // ---- フォルダー ----

    public static readonly LeftoverGuidance PackageCacheOrphan = new(
        "削除を検討",
        "ディスクの容量を使います。実害はありません。ただし、元のソフトの再インストールや修復に必要な場合があります。",
        "どのソフトの残りか分からないときは、削除せずに残してかまいません。不要と判断できるときだけ、案内コマンドで削除します。");

    public static readonly LeftoverGuidance ProductFolderOrphan = new(
        "削除を検討",
        "アンインストール後に残ったフォルダーで、再インストールやアンインストールが失敗する原因になることがあります。",
        "製品がアンインストール済みであることを確認してから、案内コマンドでフォルダーを削除します（元に戻せません）。");

    public static readonly LeftoverGuidance EmptyFolder = new(
        "対応不要",
        "実害はありません。",
        "気になる場合だけ、案内コマンドで削除してください。");

    // ---- スキャン情報 ----

    public static readonly LeftoverGuidance CheckFailed = new(
        "再実行",
        "この項目の検出結果が、一覧に含まれていません。",
        "管理者として再起動して、もう一度チェックしてください。繰り返し失敗する場合は、詳細のエラーを添えて報告してください。");

    public static readonly LeftoverGuidance Unreadable = new(
        "管理者で再確認",
        "アクセスが拒否された分は、確認できていません。残骸が見つかっていない可能性があります。",
        "「管理者として再起動」で起動し直して、もう一度チェックしてください。");
}
