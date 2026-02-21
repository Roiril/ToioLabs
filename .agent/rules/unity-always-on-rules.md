# Unity C# コード生成ルール（常時適用）

このファイルは、ToioLabs プロジェクトにおいて C# コードを生成・修正・レビューする際に **常に厳格に遵守する** ルールである。例外は認めない。

---

## 1. GC Allocation の完全回避

`Update()` / `FixedUpdate()` / `LateUpdate()` およびそこから呼ばれるメソッド内で、ヒープアロケーションを発生させてはならない。

### 禁止事項

- `new` によるオブジェクト生成（参照型）
- 文字列補間 `$""` および文字列結合 `+`
- LINQ (`Where`, `Select`, `FirstOrDefault`, `Any`, `Count()` 等)
- struct 以外の `IEnumerable` に対する `foreach`（List<T> の `foreach` は struct enumerator のため許可）
- ボクシング（`int` を `object` にキャスト等）
- `Debug.Log` を `#if UNITY_EDITOR` ガードなしで呼ぶこと

### Bad 例

```csharp
void Update()
{
    // NG: 文字列補間で毎フレーム GC Alloc
    Debug.Log($"Pos: ({_cube.x}, {_cube.y})");

    // NG: new で毎フレームヒープ確保
    var target = new Vector2Int(_cube.x, _cube.y);

    // NG: LINQ
    var active = _waypoints.Where(w => w.IsActive).ToList();

    // NG: Time.time % N パターン（精度問題 + 別ルールで禁止）
    if (Time.time % 2.0f < 0.05f)
    {
        Debug.Log("status check");
    }
}
```

### Good 例

```csharp
// フィールドに事前確保
private Vector2Int _cachedTarget;

void Update()
{
    // OK: ガード付きログ
#if UNITY_EDITOR
    Debug.Log($"Pos: ({_cube.x}, {_cube.y})");
#endif

    // OK: 事前確保した struct を再利用
    _cachedTarget.x = _cube.x;
    _cachedTarget.y = _cube.y;

    // OK: ループで代替
    for (int i = 0; i < _waypoints.Count; i++)
    {
        if (_waypoints[i].IsActive) { /* 処理 */ }
    }
}
```

---

## 2. 非同期処理の UniTask 統一

`System.Threading.Tasks` (`Task`, `Task<T>`, `Task.Delay`) の使用を禁止する。すべて `Cysharp.Threading.Tasks` (UniTask) に統一すること。

### 必須事項

- `using System.Threading.Tasks;` を書いてはならない。`using Cysharp.Threading.Tasks;` を使用すること。
- `async void` は Unity イベント関数 (`Start`, `OnEnable`, `OnDisable`, `OnDestroy`) でのみ許可する。それ以外は `async UniTask` または `async UniTaskVoid` を使用すること。
- 非同期メソッドには必ず `CancellationToken` を引数に取り、`this.GetCancellationTokenOnDestroy()` を渡すこと。
- `await Task.Delay(...)` ではなく `await UniTask.Delay(...)` を使用し、`cancellationToken` を渡すこと。

### Bad 例

```csharp
using System.Threading.Tasks; // NG: System.Threading.Tasks の使用

public class ToioRCController : MonoBehaviour
{
    // NG: async void をイベント関数以外で使用
    async void ConnectAndMove()
    {
        await Task.Delay(1000); // NG: Task.Delay
        // CancellationToken なし
    }
}
```

### Good 例

```csharp
using Cysharp.Threading.Tasks; // OK
using System.Threading;        // CancellationToken 用

namespace ToioLabs.Control
{
    public class ToioRCController : MonoBehaviour
    {
        async void Start()
        {
            // OK: Unity イベント関数内の async void
            var ct = this.GetCancellationTokenOnDestroy();
            await ConnectAndMoveAsync(ct);
        }

        // OK: async UniTask + CancellationToken
        private async UniTask ConnectAndMoveAsync(CancellationToken ct)
        {
            await UniTask.Delay(1000, cancellationToken: ct);
        }
    }
}
```

---

## 3. 名前空間の強制

新規作成するスクリプトは必ず名前空間 `ToioLabs` 配下に配置すること。グローバル名前空間への直接配置を禁止する。

### ルール

- トップレベル名前空間は `ToioLabs` とする。
- サブ名前空間はフォルダ構造に対応させる（例: `Assets/Scripts/Control/` → `ToioLabs.Control`）。
- 一般的な分類例: `ToioLabs.Control`（制御）、`ToioLabs.UI`（UI）、`ToioLabs.Data`（データ）、`ToioLabs.Editor`（エディター拡張）。

### Bad 例

```csharp
using UnityEngine;

// NG: 名前空間なしでグローバルに配置
public class ToioPatrolController : MonoBehaviour
{
}
```

### Good 例

```csharp
using UnityEngine;

namespace ToioLabs.Control
{
    // OK: 名前空間で囲まれている
    public class ToioPatrolController : MonoBehaviour
    {
    }
}
```

---

## 4. コーディング規約とカプセル化

### 命名規則（厳守）

| 対象 | 形式 | 例 |
|---|---|---|
| クラス / 構造体 / enum | `PascalCase` | `ToioPatrolController` |
| public メソッド / プロパティ | `PascalCase` | `MoveToNextWaypoint()` |
| private / protected フィールド | `_camelCase` | `_cubeManager` |
| ローカル変数 / 引数 | `camelCase` | `targetX`, `ct` |
| 定数 | `PascalCase` | `MaxSpeed` |
| イベント / コールバック | `PascalCase` | `OnTargetMoveResult` |

### Inspector 変数のカプセル化

- Inspector からアサインする変数に `public` を使用してはならない。
- 必ず `[SerializeField] private` を使用すること。
- Inspector に表示する変数には `[Header("セクション名")]` と `[Tooltip("説明")]` を付与すること。
- 読み取り専用の公開が必要な場合は、`public` プロパティ + `private` バッキングフィールドとする。

### Bad 例

```csharp
public class ToioPatrolController : MonoBehaviour
{
    // NG: public フィールドで Inspector 公開
    public Vector2[] waypoints;
    public float waitTimeSeconds = 1.0f;
    public int maxSpeed = 80;

    // NG: private フィールドの命名が _camelCase でない
    private CubeManager cubeManager;
    private bool isConnected = false;
}
```

### Good 例

```csharp
namespace ToioLabs.Control
{
    public class ToioPatrolController : MonoBehaviour
    {
        [Header("パトロール設定")]
        [Tooltip("巡回するウェイポイントの座標リスト")]
        [SerializeField] private Vector2[] _waypoints;

        [Tooltip("各ウェイポイントでの待機時間（秒）")]
        [SerializeField] private float _waitTimeSeconds = 1.0f;

        [Tooltip("移動時の最大速度")]
        [SerializeField] private int _maxSpeed = 80;

        private CubeManager _cubeManager;
        private bool _isConnected;

        // 外部から読み取りが必要な場合はプロパティで公開
        public bool IsConnected => _isConnected;
    }
}
```

---

## 5. Update() の軽量化

`Update()` メソッドは可能な限り軽量に保つこと。

### ルール

- `Update()` 内に記述してよいのは以下のみとする：
  1. 早期 return ガード（`if (_cube == null) return;`）
  2. 軽量な状態チェック・フラグ更新
  3. 入力処理 (`Input.GetKeyDown` 等)
  4. 軽量なメソッドの呼び出し
- BLE 通信、座標変換の重い計算、コレクション操作をインラインで書いてはならない。メソッドに分離すること。
- 定期実行が必要な処理には `Time.time % N` パターンを使用してはならない。次回実行時刻をフィールドにキャッシュする方式を使用すること。

### Bad 例

```csharp
void Update()
{
    if (_cube == null) return;

    _currentX = _cube.x;
    _currentY = _cube.y;

    // NG: Time.time % N は浮動小数点誤差で不安定
    if (Time.time % 2.0f < 0.05f)
    {
        Debug.Log($"[StatusCheck] X:{_cube.x} Y:{_cube.y}");
    }

    // NG: 重い計算を Update 内にインラインで記述
    float distance = 0f;
    for (int i = 0; i < _waypoints.Length - 1; i++)
    {
        distance += Vector2.Distance(_waypoints[i], _waypoints[i + 1]);
    }
}
```

### Good 例

```csharp
private float _nextLogTime;
private const float LogIntervalSeconds = 2.0f;

void Update()
{
    if (_cube == null) return;

    _currentX = _cube.x;
    _currentY = _cube.y;

    // OK: 次回実行時刻をキャッシュする方式
    if (Time.time >= _nextLogTime)
    {
        LogStatus();
        _nextLogTime = Time.time + LogIntervalSeconds;
    }

    HandleInput();
}

private void HandleInput()
{
    if (Input.GetKeyDown(KeyCode.P))
    {
        TogglePatrol();
    }
}

[System.Diagnostics.Conditional("UNITY_EDITOR")]
private void LogStatus()
{
    Debug.Log($"[StatusCheck] X:{_currentX} Y:{_currentY}");
}
```

---

## チェックリスト（コード生成・修正時に必ず確認）

コードを出力する前に、以下すべてを満たしているか確認すること。1つでも違反があれば修正してから出力すること。

- [ ] `Update` / `FixedUpdate` / `LateUpdate` 内でヒープアロケーションが発生していないか
- [ ] `Debug.Log` が `#if UNITY_EDITOR` または `[Conditional]` で保護されているか
- [ ] `using System.Threading.Tasks;` が含まれていないか
- [ ] すべての `async` メソッドが `CancellationToken` を受け取っているか（Unityイベント関数を除く）
- [ ] クラスが `namespace ToioLabs.XXX { }` で囲まれているか
- [ ] Inspector 変数が `[SerializeField] private` になっているか（`public` フィールドでないか）
- [ ] private フィールドが `_camelCase` で命名されているか
- [ ] `Time.time % N` パターンが使われていないか
- [ ] 重い処理が `Update()` から分離されているか
