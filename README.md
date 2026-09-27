# WallpaperSlideshow.at365

マルチモニター環境対応の壁紙スライドショーアプリ

- モニター単位で画像フォルダを指定。
- サブフォルダを含めて画像を探してスライドショー。
- 1枚の画像を拡大縮小して表示。複数の画像を敷き詰めていい感じに表示。
- 画像フォルダを監視して画像の追加・削除をリアルタイム反映。
- タスクトレイ常駐・単機能・軽量。

---

## 使い方

1. .NET 10 Desktop Runtime（x64）をインストール。対応OSは[MicrosoftのWindows対応表](https://learn.microsoft.com/en-us/dotnet/core/install/windows#supported-versions)を参照してください。プロジェクトの `windows7.0` はWindows 7での実行を保証する表記ではありません。
2. `WallpaperSlideshow.at365.exe` を実行。
3. トレイの「データフォルダを開く」から `config.json` を編集。初期設定の画像フォルダは空なので、使用するフォルダを指定します。
4. タスクトレイで操作：
   - **左クリック：一時停止／再開**
   - **右クリック：メニュー**

手動停止は画面ロック・解除後も維持します。RDP接続・切断中は停止し、ローカルセッションに戻ったとき、ほかの停止理由がなければ再開します。起動時は別フォルダを含めた同名プロセスを終了します。

---

## 設定ファイル（config.json）

`%AppData%\at365\WallpaperSlideshow\config.json` を読み込みます。
初回起動時に存在しなければ、埋め込みの既定設定から作成します。

```json
{
  "IntervalSeconds": 60,
  "Monitors": [
    {
      "Folder": "C:/Wallpapers/16-9",
      "Mode": "Tile",
      "TileCount": 4,
      "PaddingLeft": 0,
      "PaddingRight": 0,
      "PaddingTop": 0,
      "PaddingBottom": 40
    },
    { "Folder": "C:/Wallpapers/Monitor2", "Mode": "Fill" },
    { "Folder": "C:/Wallpapers/Monitor3", "Mode": "Fit" },
    { "Folder": "C:/Wallpapers/Monitor4", "Mode": "Center" }
  ],
  "History": {
    "Limit": 30,
    "ThumbnailWidth": 480,
    "ThumbnailHeight": 360,
    "MaxFileNameLength": 30
  }
}
```

- `IntervalSeconds`: 壁紙更新間隔（秒）
- `Monitors[n]`
  - `Folder`: モニター n に使用する画像フォルダ
    - モニター順 n は左から右への順番 (Windowsのディスプレイ設定の順番とは必ずしも一致しない)
    - サブフォルダも含めた指定フォルダ配下の画像をランダムに壁紙に設定 (1巡するまで重複なし)
    - 対象モニターの設定なし、もしくは空文字なら、そのモニターは壁紙なしで真っ黒
  - `Mode`: 画像の拡大縮小モード:
    - Tile: 複数枚の画像を敷き詰めていい感じに表示 (TileCountで枚数を指定)
    - Fill: 画面いっぱい
    - Fit: 黒帯ありで収まるように (既定)
    - Stretch: アスペクト比無視で引き伸ばし
    - Center: 中央に等倍表示
  - `TileCount`: Tileモードの場合の画像枚数
  - `PaddingLeft`, `PaddingRight`, `PaddingTop`, `PaddingBottom`: モニターのパディング
- `TileMargin`: タイル間の余白（既定10px）
- `History`: タスクトレイの履歴サムネイルの設定
  - Limit: 履歴件数
  - ThumbnailWidth: 履歴サムネイルの幅
  - ThumbnailHeight: 履歴サムネイルの高さ
  - MaxFileNameLength: 履歴ファイル名を省略する文字数
---

## 設定変更・監視の復旧・ログ

- 設定ファイルは一時停止中も約1秒ごとに確認し、同じ内容を連続して読み取れたら適用します。更新間隔の変更、別ファイルへの置換保存、削除後の再作成にも対応します。
- 実行中に設定が消えたり、不正な内容になった場合は、現在の設定を維持します。既定設定で上書きせず、次の確認で再試行します。
- 画像フォルダの監視は約5秒ごとに接続状態を確認します。フォルダの再作成や監視エラーから復旧すると画像一覧を再走査します。一時停止中は、再開後に画像一覧を反映します。
- 読み取れないサブフォルダがあっても、ほかのフォルダの画像は利用します。循環走査を防ぐため、サブフォルダのジャンクション・シンボリックリンクはたどりません。
- エラーはデータフォルダの `errors.log` に保存します。同じエラーは1分間抑制し、約1MiBで `errors.log.1` に世代交代します。ログには対象パスと例外の詳細が含まれます。
- 実行中の設定エラーはダイアログを出さずログに記録します。反映されない場合はトレイの「データフォルダを開く」からログを確認してください。

画像一覧の走査・画像読み込み・合成・BMP生成・監視の再接続はバックグラウンドで実行します。再走査の結果は次の壁紙更新時に反映します。更新処理は同時に1件とし、処理中の定期更新は重ねません。設定変更・画面変更・停止・終了後は古い結果を適用しません。履歴サムネイルもバックグラウンドで読み込みます。

OSのファイル読み込みや画像デコードは途中で即座に中断できない場合があります。その場合もUIは処理完了を待たず、古い結果は破棄します。Windowsへの壁紙設定APIの呼び出しはUIスレッドで行います。

## ビルド・テスト・配布

Windowsと.NET 10 SDKを使用します。リポジトリのルートで以下を実行します。

```powershell
dotnet build WallpaperSlideshow.at365.slnx -c Release
dotnet run --project tests/WallpaperSlideshow.RegressionTests.csproj -c Release
.\publish.bat
```

`publish\WallpaperSlideshow.at365.exe` を生成します。既定はRelease・win-x64・単一実行ファイルで、実行先には.NET 10 Desktop Runtime（x64）が必要です。アイコンと初期設定は実行ファイルに埋め込まれます。デバッグシンボルはビルド時に生成し、配布物には含めません。

更新する場合はトレイからアプリを終了し、既存の配置先を指定します。

```powershell
.\deploy.bat "C:\Tools\WallpaperSlideshow"
```

引数なしで使う場合は、`deploy.local.txt.example` を `deploy.local.txt` にコピーし、1行目に配置先の絶対パスを引用符なしで記入します。このファイルはGit管理から除外されます。以後は `.\deploy.bat` だけで配置できます。引数を指定した場合は引数が優先されます。

配置先フォルダは事前に作成してください。スクリプトはEXEを上書きし、実行中アプリの強制終了や自動起動は行いません。ユーザー設定はAppDataに保持されます。`publish.bat`へ追加のdotnetオプションを渡せますが、出力先や単一ファイル設定を変更した場合は配布方法も合わせて調整してください。

GitHub ActionsにWindowsでのビルド・回帰テスト・発行EXEの保存を設定しています。テストの範囲と実機確認項目は[tests/README.md](tests/README.md)を参照してください。

## アンインストール

- 実行ファイル郡を削除。
- `%UserProfile%\AppData\Roaming\at365\WallpaperSlideshow\` を削除。
- Windowsの壁紙設定を変更する。
