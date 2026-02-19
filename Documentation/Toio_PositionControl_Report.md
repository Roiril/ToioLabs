# toio位置制御機能 調査レポート

toio SDK for Unityを使用した、Position ID（マット座標）に基づく位置制御機能の調査結果です。

## 1. 座標情報の取得
`toio.Cube` クラスのプロパティから、マット上の絶対位置と角度を取得できます。

### プロパティ (Cubeクラス)
| プロパティ名 | 型 | 説明 |
| :--- | :--- | :--- |
| `x` | `int` | マット上のX座標 (dot単位) |
| `y` | `int` | マット上のY座標 (dot単位) |
| `angle` | `int` | マット上の角度 (0〜360度)。0度が右向き(X軸正方向)であることが多いが、マットの種類による。 |
| `pos` | `Vector2` | (x, y) をVector2で取得 |

### イベントハンドラ
座標更新時に発火する `idCallback` は存在しますが、デフォルトでは `NotSupported` となっている場合があるため、毎フレーム `Update` で `cube.x`, `cube.y` をポーリングするのが一般的です。

### マットから外れた（読み取り不能）検知
座標が読み取れなくなった（ID Missed）場合、以下の方法で検知可能です。

1.  **コールバック**: `cube.idMissedCallback` にリスナーを登録する。
2.  **`TargetMove` の応答**: 移動中に外れた場合、コールバックで `TargetMoveRespondType.ToioIDmissed` が返る。

## 2. 座標指定移動 (TargetMove)
指定した座標へ自動的に移動させる `TargetMove` メソッドが `Cube` クラスに定義されています。

```csharp
public virtual void TargetMove(
    int targetX,                // 目標X座標
    int targetY,                // 目標Y座標
    int targetAngle,            // 目標角度
    int configID = 0,           // 制御ID（応答時に識別用）
    int timeOut = 0,            // タイムアウト時間（0は設定なし）
    TargetMoveType targetMoveType = TargetMoveType.RotatingMove, // 移動タイプ（回転しながら、回転してから等）
    int maxSpd = 80,            // 最大速度 (8~115)
    TargetSpeedType targetSpeedType = TargetSpeedType.UniformSpeed, // 速度変化（一定、加速、減速など）
    TargetRotationType targetRotationType = TargetRotationType.AbsoluteLeastAngle, // 回転方向
    ORDER_TYPE order = ORDER_TYPE.Strong // 優先度
)
```

### 到着判定・移動の中断
`targetMoveCallback` イベントを使用します。

```csharp
// リスナー登録
cube.targetMoveCallback.AddListener("MyListener", OnTargetMoveResult);

// コールバックメソッド
void OnTargetMoveResult(Cube cube, int configID, Cube.TargetMoveRespondType response)
{
    if (response == Cube.TargetMoveRespondType.Normal) {
        // 到着成功
    } else if (response == Cube.TargetMoveRespondType.ToioIDmissed) {
        // マットから外れた
    }
}
```

移動の中断（キャンセル）は、新しい `Move`（速度指定）や `TargetMove` 命令を上書き送信することで行います。

## 3. マットの座標系 (Mat.cs)
`toio.Simulator.Mat` クラスに定義されている座標範囲です。

*   **トイオ・コレクション（土俵面）**:
    *   Rect: (45, 45) - (455, 455)  ※幅410
    *   X: 45 ~ 455, Y: 45 ~ 455
*   **簡易プレイマット（#1）**:
    *   Rect: (98, 142) - (402, 358)

単位は toio ID の「dot」です（約0.56mm/dotではない場合もあるので注意が必要ですが、Unity SDK上ではこの座標値を使用します）。

## 4. 実装の参考例
SDK内の以下のファイルが参考になります。

*   **パス**: `Assets/toio-sdk/Samples/Sample_Motor/Sample_Motor.cs`
*   **内容**:
    *   `TargetMove` を使用して指定座標へ移動。
    *   `targetMoveCallback` で到着を確認し、次の動作へ遷移。
    *   `Update` 内で `cube.x`, `cube.y` を監視して範囲外判定を行う例もあり。

```csharp
// Sample_Motor.cs からの抜粋イメージ
cubes[0].TargetMove(targetX:250, targetY:250, targetAngle:270, configID:0);

void TargetMoveCallback(Cube cube, int configID, Cube.TargetMoveRespondType response) {
    if (response == Cube.TargetMoveRespondType.Normal) {
        // 到着処理
    }
}
```
