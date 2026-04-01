# WallpaperSlideshow365

マルチモニター環境向けの壁紙スライドショーアプリです。  

- モニター単位で画像フォルダを指定。
- サブフォルダを含めて画像を探してスライドショー。
- 画像フォルダを監視して画像の追加・削除をリアルタイム反映。
- タスクトレイ常駐・単機能・軽量

---

## 設定ファイル（config.json）

`%UserProfile%\AppData\Roaming\at365\WallpaperSlideshow\config.json` に配置される JSON ファイルが優先して読み込まれます。
なければ、アプリケーションの実行ファイルと同じフォルダにある `config.json` が読み込まれます。

```json
{
  "IntervalSeconds": 60,
  "TileCount": 12,
  "Monitors": [
    {
      "Folder": "C:/Wallpapers/16-9",
      "Mode": "Tile",
      "PaddingLeft": 0,
      "PaddingRight": 0,
      "PaddingTop": 0,
      "PaddingBottom": 40
    },
    { "Folder": "C:/Wallpapers/Monitor2", "Mode": "Fill" }
    { "Folder": "C:/Wallpapers/Monitor3", "Mode": "Fit" },
    { "Folder": "C:/Wallpapers/Monitor4", "Mode": "Center" }
  ]
}
```

- `IntervalSeconds`: 壁紙更新間隔（秒）
- `TileCount`: Tileモードの表示枚数
- `Monitors[n].Folder`: モニター n に使用する画像フォルダ
  - モニター順 n は左から右への順番 (Windowsのディスプレイ設定の順番とは必ずしも一致しない)
  - サブフォルダも含めた指定フォルダ配下の画像をランダムに壁紙に設定 (1巡するまで重複なし)
  - 対象モニターの設定なし、もしくは空文字なら、そのモニターは壁紙なしで真っ黒
- `Monitors[n].Mode`: 画像の拡大縮小モード: 
  - Fill: 画面いっぱい
  - Fit: 黒帯ありで収まるように (既定)
  - Stretch: アスペクト比無視で引き伸ばし
  - Center: 中央に等倍表示
  - Tile:   タイル表示

---

## 使い方

1. `config.json` を編集
2. `WallpaperSlideshow365.exe` を実行  
3. タスクトレイに常駐  
   - **左クリック：一時停止／再開**
   - **右クリック：終了**
