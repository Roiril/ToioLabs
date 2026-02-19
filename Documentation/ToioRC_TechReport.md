# toioラジコン化プロジェクト 開発完了レポート

## 1. プロジェクト概要
Unityエディタ上のキーボード入力（矢印キー/WASD）を用いて、toioコアキューブ（実機）をリアルタイムに操作するラジコン（RC）機能を実装しました。
toio SDK for Unityを活用し、Bluetooth (BLE) 経由での接続とモーター制御を実現しています。

## 2. 技術スタック
*   **Unity**: 2022.3.x (URP)
*   **Language**: C#
*   **Hardware**: toio Core Cube
*   **SDK**: toio SDK for Unity
    *   **`CubeManager`**: キューブの接続管理（Scanner, Connecterのラップ）。
    *   **`Cube`**: 個別のキューブ操作（Move, Light, Soundなど）を行うクラス。
    *   **`UniTask`**: 非同期接続処理（`async/await`）に使用。

## 3. 実装のポイント

### 3.1 差動二輪の制御ロジック
toioは左右のタイヤを個別に制御する「差動二輪駆動」ロボットです。
Unityの `Input.GetAxis`（前後・回転の2軸入力）を、左右それぞれのモーター出力に変換するために以下の計算式を採用しました。

```csharp
// vertical: 前後入力 (-1.0 ~ 1.0)
// horizontal: 回転入力 (-1.0 ~ 1.0)

// 左モーター: 前進成分 + 回転成分
// (右旋回時は左モーターを速くする)
int left = (int)Mathf.Clamp((vertical + horizontal) * 100, -100, 100);

// 右モーター: 前進成分 - 回転成分
// (右旋回時は右モーターを減速/逆転させる)
int right = (int)Mathf.Clamp((vertical - horizontal) * 100, -100, 100);
```

### 3.2 通信負荷の制御
BLE通信の帯域圧迫とキューブ側の処理負荷を考慮し、`Update` メソッド内で毎フレーム命令を送信するのではなく、意図的に通信間隔を制御しています。

*   **送信間隔**: 50ms（0.05秒）に1回
*   **命令持続時間**: 100ms
    *   通信のジッター（ゆらぎ）で次の命令が遅れても動きが止まらないよう、送信間隔より長めの持続時間を設定しています。

## 4. トラブルシューティング（重要）

### 現象
スクリプト実行時、`CubeManager.MultiConnect()` がタイムアウトし、ログに `No cubes found` と表示され実機と接続できない。

### 原因
SDKのデフォルト挙動または自動判定において、Unityエディタ上では「シミュレーター（CubeUnity）」の検出が優先される場合があるため。
ログに `CubeScanner/SimImpl` が出力されており、実機（BLE）のスキャンが行われていませんでした。

### 解決策
`CubeManager` の初期化時に、**`ConnectType.Real`** を明示的に指定することで、強制的に実機接続モードで動作させました。

```csharp
// 【修正前】自動判定（シミュレーター優先になる場合あり）
cubeManager = new CubeManager();

// 【修正後】実機BLE接続を強制
cubeManager = new CubeManager(ConnectType.Real);
```

## 5. 今後の展望
1.  **速度調整機能**: Shiftキー押し下げ時は倍速にする、またはUIスライダーで最高速度を変更する機能。
2.  **センサー情報の可視化**: `cube.x`, `cube.y`（マット上の座標）や `cube.isCollisionDetected`（衝突判定）をリアルタイムにUnity UIへ表示する。
3.  **複数台制御**: `MultiConnect(2)` などで複数台を接続し、群制御や追従走行を実装する。
