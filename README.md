# WallpaperSlideshow365

複数モニター環境向けの壁紙スライドショーアプリです。  
各モニターごとに別フォルダの画像をランダムに選択し、  
全モニターの全体を 1 枚の壁紙として合成して設定します。

タスクトレイ常駐。
終了時に壁紙を真っ黒画像に戻す。

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
  - サブフォルダも含めた指定フォルダ配下の画像をランダムに壁紙に設定 (1巡するまで重複なし)
  - 対象モニターの設定なし、もしくは空文字なら、そのモニターは壁紙なしで真っ黒

---

## ▶ 使い方

1. `config.json` を用意  
2. `running.ico` と `paused.ico` をアプリと同じフォルダに置く  
3. `WallpaperSlideshow365.exe config.json` を実行  
4. タスクトレイに常駐  
   - **左クリック：一時停止／再開**
   - **右クリック：終了**

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
