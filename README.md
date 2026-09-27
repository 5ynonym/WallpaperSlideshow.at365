# WallpaperSlideshow365

マルチモニター環境対応の壁紙スライドショーアプリ

- モニター単位で画像フォルダを指定。
- サブフォルダを含めて画像を探してスライドショー。
- 1枚の画像を拡大縮小して表示。複数の画像を敷き詰めていい感じに表示。
- 画像フォルダを監視して画像の追加・削除をリアルタイム反映。
- タスクトレイ常駐・単機能・軽量。

---

## 使い方

1. 設定ファイル `config.json` を編集
2. `WallpaperSlideshow365.exe` を実行  
3. タスクトレイに常駐  
   - **左クリック：一時停止／再開**
   - **右クリック：メニュー**

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
  },
}
```

- `IntervalSeconds`: 壁紙更新間隔（秒）
- `Monitors[n]`
  - .Folder`: モニター n に使用する画像フォルダ
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

再走査の結果は次の壁紙更新時に反映します。低速フォルダの走査・描画を非同期にする対応は未実装です。

## アンインストール

- 実行ファイル郡を削除。
- `%UserProfile%\AppData\Roaming\at365\WallpaperSlideshow\` を削除。
- Windowsの壁紙設定を変更する。
