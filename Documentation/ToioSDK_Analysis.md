# toio SDK for Unity 調査レポート

## 1. 主要なクラスの特定

キューブとの接続や操作を行うための主要なクラスは以下の通りです。

*   **接続・管理クラス**: `CubeManager` (`toio` 名前空間)
    *   複数のキューブの接続、再接続、切断を一元管理するクラスです。通常はこのクラスを使用することが推奨されます。
    *   低レベルな接続制御には `CubeConnecter` も存在しますが、`CubeManager` がこれをラップしています。
*   **キューブインターフェース**: `Cube` (`toio` 名前空間)
    *   個々のキューブを操作するための抽象基底クラスです。
    *   実機 (`CubeReal`) とシミュレーター (`CubeUnity`) の両方を同一のコードで扱えるように設計されています。

## 2. 移動制御APIの仕様

モーターを制御するための主要なメソッドは `Cube` クラスに定義されています。

### `Move` メソッド (時間指定なし/あり モーター制御)
直接左右のモーターの速度を指定して移動させます。

```csharp
public virtual void Move(int left, int right, int durationMs, ORDER_TYPE order = ORDER_TYPE.Weak)
```

*   **`left`**: 左モーターの速度 (-100 ～ 100)。負の値で後退。
*   **`right`**: 右モーターの速度 (-100 ～ 100)。負の値で後退。
*   **`durationMs`**: 持続時間（ミリ秒）。0を指定すると、次の命令が来るまで動き続けます。
*   **`order`**: 命令の優先度（`Strong` または `Weak`）。通常は `Weak` で問題ありませんが、即座に反映させたい場合は `Strong` を使用します。

※ 他にも、マット上の座標を指定して移動する `TargetMove` や、加速度を指定する `AccelerationMove` も存在します。

## 3. サンプルコードの有無

ラジコン操作（モーターの直接制御）の参考になるサンプルは以下が最適です。

*   **パス**: `Assets/toio-sdk/Samples/Sample_Motor/Sample_Motor.cs`
*   **内容**: `TargetMove` や `AccelerationMove` を使用したデモですが、`CubeManager` を使った接続からループ内での制御までの流れが理解できます。

接続周りのよりシンプルなサンプルは `Assets/toio-sdk/Samples/Sample_ConnectName` などにあります。

## 4. 最小構成のフロー (接続 → 前進 → 切断)

`CubeManager` を使用した、最もシンプルな実装例です。

```csharp
using UnityEngine;
using toio;
using Cysharp.Threading.Tasks; // UniTaskを使用

public class SimpleToioControl : MonoBehaviour
{
    CubeManager cubeManager;
    Cube myCube;

    async void Start()
    {
        // 1. 接続 (自動接続モード)
        cubeManager = new CubeManager();
        myCube = await cubeManager.SingleConnect();

        if (myCube != null)
        {
            Debug.Log("接続成功: " + myCube.localName);
            
            // 2. 前進命令 (左:50, 右:50, 2秒間)
            // Moveは非同期メソッドではないため、待ち時間は別途設ける必要があります
            myCube.Move(50, 50, 2000, Cube.ORDER_TYPE.Strong);

            // 2秒待機 (UniTask)
            await UniTask.Delay(2000);

            // 3. 切断
            cubeManager.Disconnect(myCube);
            Debug.Log("切断しました");
        }
    }
}
```

### ポイント
*   `Start` メソッドを `async` にして `await` を使えるようにしています。
*   `SingleConnect()` で最も近くにあるキューブ1台に接続します。
*   `Move` メソッド自体は「命令を送信する」だけなので、動作完了を待つには `UniTask.Delay` などで待機する必要があります。
