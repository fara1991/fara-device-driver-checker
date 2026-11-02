# FaraDeviceDriverChecker

Windowsのデバイスドライバーの状態を診断し、必要に応じてWindows Updateを通じて更新をサポートするGUIツールです。

## 技術スタック

- **.NET 9.0** (WPF)
- **Material Design In XAML Toolkit** - モダンなUI
- **WMI (Windows Management Instrumentation)** - デバイス情報の取得
- **Windows Update API** - ドライバー更新のインストール

## 主な機能

- **マルチカテゴリ診断**: 以下の主要なデバイスカテゴリを複数同時にスキャンできます。
  - オーディオ
  - ディスプレイ
  - ネットワーク
  - Bluetooth
  - USB
  - キーボード・マウス
  - カメラ
- **デバイス状態の可視化**: デバイスマネージャーのステータスに基づき、「正常」と「問題あり」の状態を、アイコンの形と色で分かりやすく表示します。
- **ドライバー更新**: Windows Update APIを利用して、ドライバー更新プログラムをインストール。
- **Windows Update連携**: アプリケーション内から直接Windows Updateの設定を開いたり、更新を試行したりすることが可能。
- **アンインストール漏れ（残骸）チェック**: アンインストールできない原因になる残骸を検出（読み取り専用）。詳細は下記。
- **エクスポート**: 診断結果をテキスト / JSONで保存。

## アンインストール漏れ（残骸）チェック

「アンインストール漏れチェック」タブで、アンインストールに失敗する原因になりやすい残骸を検出して一覧表示します。
Intel Rapid Storage Technologyが、ドライバーストアに無い `iaAHCIC.inf` の削除に失敗（`DiUninstallDriver` が0x490）して
アンインストールできなくなった事例を一般化したものです。

**このチェックは読み取り専用です。レジストリ・ファイル・サービスの変更や削除は一切自動実行しません。**
検出結果ごとに「案内コマンド」を表示し、コピーして手動で実行できるようにしています（レジストリは `reg export` でのバックアップ付き）。

### 検出する項目

| 分類 | 内容 |
|---|---|
| 存在しないINFの参照 | MSI（キャッシュ済み `.msi` のFileテーブル）が同梱する `.inf` がドライバーストアに無い／デバイスが `C:\Windows\INF\oemNN.inf` を参照しているのに実体が無い |
| サービスの残り | 実行ファイルが存在しないサービス、Program Files配下で「停止・無効」のまま残るサービス（`IAStorDataMgrSvc` など） |
| フォルダーの残り | 登録の無い `ProgramData\Package Cache\{GUID}`、既知パターン（Intel RSTのプログラムフォルダー）、ファイルが1つも無い空のベンダーフォルダー |
| レジストリの残り | HKLM / WOW6432Node / HKCUの `Uninstall`（アンインストーラーやインストール先が無い、MSIなのに `Installer\Products` の登録が無い）、`Installer\Products` と `Installer\UserData`（キャッシュMSIが無い、Productsにだけ残る）。どちら側のビューにあるかも表示します |
| 旧版ドライバーの重複 | `pnputil /enum-drivers` 相当で同名INFの旧版を検出（使用中のものは対象外） |
| 問題のあるデバイス | `Get-PnpDevice -PresentOnly` でStatusがOK以外に相当するデバイス（WMIの `Win32_PnPEntity`） |

### 使い方

1. 「アンインストール漏れチェック」タブで「残骸をチェック」を押します。
2. 一覧は、キーワード（空白区切りでAND。内容・対象・詳細・分類を検索）、重要度、分類で絞り込めます。「クリア」で解除します。
   行を選ぶと、下に詳細と案内コマンドが表示されます。「コマンドをコピー」でクリップボードにコピーできます。
3. コマンドは内容と対象を必ず確認し、**管理者権限のPowerShell／コマンドプロンプトで手動実行**してください。
4. 画面右上の「エクスポート」で、診断結果（ドライバー診断と残骸チェック）をテキストまたはJSONに保存できます。
5. `Installer\UserData` の読み取りには管理者権限が必要な場合があります。「管理者として再起動」（確認ダイアログあり）で起動し直すと、より多く確認できます。

### 安全方針と限界

- ストレージ系ドライバー（`iastorvd`、`iaAHCIC`、`stornvme` などのINF、ディスク／SCSI／IDE ATA／ボリューム系のクラス）は、
  削除すると起動できなくなるおそれがあるため、**検出しても削除コマンドを提案しません**（画面にもその旨を表示します）。
- 旧版ドライバーの削除案内は、どのデバイスにも使われていない非ストレージ系のものに限り、`/force` を付けない `pnputil /delete-driver` です。
- MSIが参照するINFの検出は、MSIのFileテーブルにある `.inf` を基にした推定です。誤検出を避けるため、対象は次の2つに限ります。ストレージ系のINFと、DIFxのドライバー登録テーブル（`MsiDriverPackages`）を持つMSIのINFです。
- 空フォルダー・停止無効サービスなどは、利用者が意図して残している場合もあります。

## インストール

インストールは不要です。

1. [最新リリース](https://github.com/fara1991/fara-windows-checker/releases)から `FaraDeviceDriverChecker-vX.X.X-win-x64.zip` をダウンロード
2. ZIPファイルを解凍
3. `FaraDeviceDriverChecker.exe` を実行

## 使い方

1. **カテゴリの選択**: 左側のカテゴリリストから、診断したいデバイスカテゴリをチェックボックスで選択します（複数選択可）。「すべて」をチェックすると全カテゴリが選択されます。
2. **スキャンの実行**: 「スキャン」ボタンをクリックすると、選択したカテゴリのデバイス一覧と状態が表示されます。
3. **更新の実行**: 問題があるデバイスがある場合、「ドライバーを更新」ボタンからドライバーの更新をインストールできます。
4. **設定へのアクセス**: 「Windows Updateの設定を開く」ボタンから、システムのWindows Update設定を直接開くことができます。

## 結果の見方

各デバイスのステータスに応じて、以下のアイコンが表示されます:

| アイコン | 状態 | 説明 |
|:---:|:---:|:---|
| 緑のチェックマーク | 正常 | デバイスは正しく認識され、問題なく動作しています |
| 赤の警告マーク | 問題あり | デバイスに問題が発生しています |

## システム要件

- **OS**: Windows 10以降 (64bit)
- **ランタイム**: 不要（リリース版は単一実行ファイルとして配布）
- **権限**: ドライバー更新を実行する場合は、管理者権限が必要です

## トラブルシューティング

### プログラムが起動しない
- セキュリティソフトによってブロックされている場合は、実行を許可してください。
- Windows SmartScreenが警告を表示する場合は、「詳細情報」から実行を許可できます。

### デバイスが表示されない
- デバイスが物理的に接続されているか確認してください。
- デバイスマネージャーでデバイスが認識されているか確認してください。

### ドライバー更新が失敗する
- 管理者権限でアプリケーションを実行してください。
- Windows Updateサービスが動作しているか確認してください。

## 注意事項

- このツールはデバイスの状態を確認し、標準的なWindows Updateの機能を呼び出すものです。
- 診断結果に基づいてドライバーを更新する際は、事前にシステムの復元ポイントを作成することを推奨します。
- 企業環境では、IT管理者に相談してからドライバー更新を行ってください。

## プロジェクト構成

```
FaraDeviceDriverChecker/
├── Models/
│   ├── DeviceInfo.cs        # デバイス情報モデル
│   ├── DeviceCategory.cs    # デバイスカテゴリ定義
│   └── LeftoverItem.cs      # 残骸チェックの検出項目
├── ViewModels/
│   └── MainViewModel.cs     # メインビューモデル (MVVM)
├── Services/
│   ├── DeviceService.cs     # デバイス情報取得・更新サービス
│   ├── LeftoverScanner.cs   # 残骸チェック（読み取り専用）
│   ├── PnpUtilParser.cs     # pnputil /enum-driversの解析
│   └── ReportExporter.cs    # テキスト / JSONエクスポート
├── Utilities/
│   ├── WmiHelper.cs         # WMIクエリヘルパー
│   ├── ErrorCodeHelper.cs   # エラーコード変換
│   ├── MsiGuid.cs           # MSI圧縮GUIDの相互変換
│   ├── MsiDatabase.cs       # キャッシュMSIの読み取り
│   └── StorageGuard.cs      # ストレージ系の保護判定
├── MainWindow.xaml          # メインウィンドウUI
└── App.xaml                 # アプリケーション設定
```

## ビルド方法

### 必要条件

- .NET 9.0 SDK
- Windows 10以降

### テスト

```bash
dotnet test FaraDeviceDriverChecker.Tests
```

実機の読み取り専用チェックを含むため、Windows上で実行してください。

### ビルド手順

```bash
# リポジトリをクローン
git clone https://github.com/fara1991/fara-windows-checker.git
cd fara-windows-checker

# 依存関係の復元
dotnet restore

# デバッグビルド
dotnet build

# リリースビルド (単一実行ファイル)
dotnet publish FaraDeviceDriverChecker/FaraDeviceDriverChecker.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish
```

## サポート

問題や質問がある場合は、[GitHubのIssues](https://github.com/fara1991/fara-windows-checker/issues)で報告してください。
報告時には、Windowsのバージョンと発生している現象の詳細を添えていただけますと幸いです。

## ライセンス

MIT License
