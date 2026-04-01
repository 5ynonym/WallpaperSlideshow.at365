# WallpaperSlideshow365

マルチモニター環境対応の壁紙スライドショーアプリ

- モニター単位で画像フォルダを指定。
- サブフォルダを含めて画像を探してスライドショー。
- 1枚の画像を拡大縮小して表示。複数の画像を敷き詰めていい感じに表示。
- 画像フォルダを監視して画像の追加・削除をリアルタイム反映。
- タスクトレイ常駐・単機能・軽量。

---

## 設定ファイル（config.json）

`%UserProfile%\AppData\Roaming\at365\WallpaperSlideshow\config.json` に配置される JSON ファイルが優先して読み込まれます。
なければ、アプリケーションの実行ファイルと同じフォルダにある `config.json` が読み込まれます。

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
    { "Folder": "C:/Wallpapers/Monitor2", "Mode": "Fill" }
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
- `Monitors[n]
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

## 使い方

1. `config.json` を編集
2. `WallpaperSlideshow365.exe` を実行  
3. タスクトレイに常駐  
   - **左クリック：一時停止／再開**
   - **右クリック：メニュー**

---

## アンインストール

- 実行ファイル郡を削除。
- `%UserProfile%\AppData\Roaming\at365\WallpaperSlideshow\` を削除。
- Windowsの壁紙設定を変更する。
