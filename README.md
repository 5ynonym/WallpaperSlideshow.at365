# WallpaperSlideshow365

複数モニター環境向けの壁紙スライドショーアプリです。  
各モニターごとに別フォルダの画像をランダムに選択し、  
仮想デスクトップ全体を 1 枚の壁紙として合成して設定します。

タスクトレイ常駐型で、**一時停止／再開のトグル**、  
**解像度・スケーリング変更時の自動再起動** に対応しています。

---

## ✨ 主な機能

### 🖥 マルチモニター対応
- モニターごとに別フォルダを指定可能  
- 画像はランダムシャッフル  
- 直前と同じ画像を避けるロジック  
- 全モニターを 1 枚の壁紙に合成して設定

### ⏸ 一時停止・再開（トレイアイコン左クリック）
- 左クリックで **一時停止／再開をトグル**
- 一時停止中は壁紙を **真っ黒** に固定
- 稼働中／停止中で **トレイアイコンを切り替え**

### 🔄 解像度・スケーリング変更時の自動再起動
- `SystemEvents.DisplaySettingsChanged` を監視  
- 解像度変更・DPI変更・モニター追加/削除・配置変更を検知  
- 自動でアプリを再起動し、正しいレイアウトで壁紙を再生成

### 🧹 フェールセーフ
- 壁紙設定後に黒画像で上書きし、  
  Windows が壁紙をロックしても破損しないように保護

---

## 📁 設定ファイル（config.json）

```json
{
  "IntervalSeconds": 60,
  "Monitors": [
    { "Folder": "C:/Wallpapers/Monitor1" },
    { "Folder": "C:/Wallpapers/Monitor2" }
  ]
}
```

- `IntervalSeconds`  
  壁紙更新間隔（秒）
- `Monitors[n].Folder`  
  モニター n に使用する画像フォルダ  
  ※モニター数より少ない場合は黒塗り

---

## ▶ 使い方

1. `config.json` を用意  
2. `running.ico` と `paused.ico` をアプリと同じフォルダに置く  
3. `WallpaperSlideshow365.exe config.json` を実行  
4. タスクトレイに常駐  
   - **左クリック：一時停止／再開**
   - **右クリック：終了**

---

## 🧠 内部仕様（技術メモ）

### 壁紙の合成
- `Screen.AllScreens` から仮想デスクトップ全体の矩形を取得  
- 各モニターの画像をスケールして黒背景に描画  
- `merged_wallpaper.jpg` として保存  
- `SystemParametersInfo(SPI_SETDESKWALLPAPER)` で壁紙に設定

### 一時停止
- タイマー停止  
- 黒画像を壁紙に設定  
- アイコンを paused.ico に変更  

### 再開
- タイマー再開  
- アイコンを running.ico に変更  

### 解像度・DPI 変更
- `SystemEvents.DisplaySettingsChanged` をフック  
- プロセスを自動再起動して再初期化

---

## 📜 ライセンス

```
Copyright 2026 5ynonym

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
```

```
