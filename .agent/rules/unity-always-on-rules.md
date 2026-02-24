# Unity C# コード生成ルール（常時適用）

このファイルは、ToioLabs プロジェクトにおいて C# コードを生成・修正・レビューする際に **常に厳格に遵守する** ルールである。例外は認めない。

> **このファイルの使い方:** コードを 1 行でも書く前に、まず末尾の「最終チェックリスト」を開き、書いた後にもう一度チェックする。

---

## 1. GC Allocation の完全回避

### 判断フロー

```
コードを書こうとしている
  │
  ├─ そのコードは Update / FixedUpdate / LateUpdate 内、
  │  またはそこから呼ばれるメソッド内か？
  │    │
  │    ├─ YES → 以下のチェックをすべて通すこと
  │    │    │
  │    │    ├─ `new` で参照型を作っていないか？ → 作っていたら NG。フィールドに事前確保する
  │    │    ├─ `$""` や `+` で文字列を作っていないか？ → 作っていたら NG。キャッシュ文字列かフィールドを使う
  │    │    ├─ LINQ を使っていないか？（Where, Select, Any, Count() 等） → 使っていたら NG。for ループに書き換える
  │    │    ├─ struct 以外の IEnumerable に foreach していないか？ → していたら NG。for ループに書き換える
  │    │    ├─ int を object にキャストしていないか？（ボクシング） → していたら NG
  │    │    └─ Debug.Log を `#if UNITY_EDITOR` なしで書いていないか？ → 書いていたら NG。ガードを付ける
  │    │
  │    └─ すべて OK → 書いてよい ✅
  │
  └─ NO（Awake, Start, コールバック等） → GC Alloc は許容される ✅
```

### Bad → Good 変換表

| やりがちな NG コード | 正しい書き方 |
|:--|:--|
| `Debug.Log($"Pos: ({x}, {y})");` | `#if UNITY_EDITOR` で囲む |
| `var target = new Vector2Int(x, y);` | フィールドに `_cachedTarget` を持ち、`.x` `.y` を書き換える |
| `_waypoints.Where(w => w.IsActive).ToList()` | `for (int i = 0; ...)` ループで代替 |
| `if (Time.time % 2.0f < 0.05f)` | `if (Time.time >= _nextTime) { _nextTime = Time.time + 2f; }` |

---

## 2. 非同期処理の UniTask 統一

### 判断フロー

```
async メソッドを書こうとしている
  │
  ├─ using System.Threading.Tasks; を書いていないか？
  │    ├─ 書いていた → 削除して using Cysharp.Threading.Tasks; に変える
  │    └─ 書いていない → OK
  │
  ├─ メソッドの戻り値は何か？
  │    ├─ async void →  Unity イベント関数 (Start, OnEnable 等) か？
  │    │    ├─ YES → 許可。ただし中で async UniTask を呼ぶ形にする
  │    │    └─ NO → NG。async UniTask か async UniTaskVoid に変える
  │    ├─ async Task → NG。async UniTask に変える
  │    └─ async UniTask → OK ✅
  │
  ├─ CancellationToken を受け取っているか？
  │    ├─ YES → OK ✅
  │    └─ NO → Unity イベント関数 (Start 等) か？
  │         ├─ YES → 中で this.GetCancellationTokenOnDestroy() を取得して渡す
  │         └─ NO → 引数に CancellationToken ct を追加する
  │
  └─ await Task.Delay(...) を使っていないか？
       ├─ 使っていた → await UniTask.Delay(..., cancellationToken: ct) に変える
       └─ 使っていない → OK ✅
```

---

## 3. 名前空間の強制

### 判断フロー

```
新しい .cs ファイルを作る
  │
  ├─ namespace で囲んでいるか？
  │    ├─ YES → namespace は ToioLabs.XXX か？
  │    │    ├─ YES → OK ✅
  │    │    └─ NO → ToioLabs をトップレベルにする
  │    └─ NO → NG。必ず namespace ToioLabs.XXX { } で囲む
  │
  └─ サブ名前空間はフォルダに対応しているか？
       ├─ Assets/Scripts/Control/ → ToioLabs.Control
       ├─ Assets/Scripts/UI/ → ToioLabs.UI
       ├─ Assets/Scripts/Data/ → ToioLabs.Data
       └─ Assets/Scripts/Editor/ → ToioLabs.Editor
```

---

## 4. コーディング規約とカプセル化

### 命名の判断フロー

```
変数・メソッド・クラスの名前を付ける
  │
  ├─ クラス / struct / enum → PascalCase（例: ToioPatrolController）
  ├─ public メソッド / プロパティ → PascalCase（例: MoveToNextWaypoint()）
  ├─ private / protected フィールド → _camelCase（例: _cubeManager）
  ├─ ローカル変数 / 引数 → camelCase（例: targetX, ct）
  ├─ 定数 → PascalCase（例: MaxSpeed）
  └─ イベント / コールバック → PascalCase（例: OnTargetMoveResult）
```

### Inspector 変数の判断フロー

```
Inspector からアサインしたい変数がある
  │
  ├─ public フィールドにしようとしていないか？
  │    ├─ YES → NG。[SerializeField] private に変える
  │    └─ NO → OK
  │
  ├─ [Header("セクション名")] を付けたか？
  │    ├─ YES → OK
  │    └─ NO → 付ける
  │
  ├─ [Tooltip("説明")] を付けたか？
  │    ├─ YES → OK
  │    └─ NO → 付ける
  │
  └─ 外部から読み取りが必要か？
       ├─ YES → public プロパティ (get only) + private バッキングフィールド
       └─ NO → [SerializeField] private だけで OK ✅
```

---

## 5. Update() の軽量化

### 判断フロー

```
Update() にコードを書こうとしている
  │
  ├─ それは以下のどれに該当するか？
  │    ├─ 早期 return ガード (if (_cube == null) return;) → OK ✅
  │    ├─ 軽量な状態チェック・フラグ更新 → OK ✅
  │    ├─ 入力処理 (Input.GetKeyDown 等) → OK ✅
  │    ├─ 軽量なメソッド呼び出し → OK ✅
  │    └─ それ以外（BLE通信、重い計算、コレクション操作） → NG。メソッドに分離する
  │
  ├─ 定期実行が必要か？
  │    ├─ YES → Time.time % N を使おうとしていないか？
  │    │    ├─ 使おうとしていた → NG。次回時刻キャッシュ方式に変える
  │    │    │    _nextTime フィールドを作り、if (Time.time >= _nextTime) で判定
  │    │    └─ キャッシュ方式を使っている → OK ✅
  │    └─ NO → OK ✅
  │
  └─ Debug.Log を書こうとしていないか？
       ├─ 書こうとしていた → #if UNITY_EDITOR で囲む、またはメソッドに [Conditional("UNITY_EDITOR")] を付ける
       └─ 書いていない → OK ✅
```

---

## 6. UI イベント委譲パターン

### 判断フロー

```
UI のクリック / ドラッグを検出したい
  │
  ├─ その UI 要素はシーンに最初から存在するか？
  │    ├─ YES → そのオブジェクト自身に IPointerDownHandler を実装してよい ✅
  │    └─ NO（ランタイムで動的に生成される）
  │         │
  │         ├─ コントローラー本体に IPointerDownHandler を実装しようとしていないか？
  │         │    ├─ YES → NG ❌ 動的に生成した子要素のクリックは検出できない
  │         │    └─ NO → OK
  │         │
  │         └─ 正しい方法:
  │              1. 薄い受信コンポーネント (例: TouchInputReceiver) を作る
  │                 - IPointerDownHandler, IDragHandler を実装
  │                 - event Action<Vector2> OnInput を公開
  │              2. 動的要素に AddComponent<TouchInputReceiver>() でアタッチ
  │              3. コントローラーは OnInput イベントを購読する
  │              4. 購読解除は必ず行う（メモリリーク防止）
  │
  │ さらに注意:
  │    ├─ その要素の Image.raycastTarget = true になっているか？ → false だとクリック不可
  │    └─ 親に CanvasGroup があるか？
  │         ├─ YES → blocksRaycasts = true / interactable = true になっているか？
  │         │    ├─ NO → false のままだと子要素すべてのクリックが無効化される ❌
  │         │    └─ YES → OK ✅
  │         └─ NO → OK ✅
```

---

## 7. ランタイム再親付けと復帰パターン

### 判断フロー

```
ランタイムで transform.SetParent() を使おうとしている
  │
  ├─ 元の親を Awake でキャッシュしているか？
  │    ├─ YES → OK
  │    └─ NO → _originalParent = transform.parent; を Awake に追加する
  │
  ├─ リセット / 再キャリブレーション時に元に戻せるか？
  │    ├─ YES → transform.SetParent(_originalParent, false); を書いているか確認
  │    └─ NO → 復帰ロジックを追加する
  │
  └─ worldPositionStays の引数は何にしているか？
       ├─ true → 3D オブジェクトでワールド座標を維持したい場合のみ
       └─ false → UI の場合はほぼこちら（ローカル座標を維持）✅
```

---

## 8. GC-Free アニメーションキュー

### 判断フロー

```
Update() 駆動のアニメーションを実装する
  │
  ├─ そのアニメーションは 1 つだけ再生すれば十分か？
  │    ├─ YES → bool _animating + float _startTime で管理すればよい ✅
  │    └─ NO（複数リクエストが連続で来る可能性がある）
  │         │
  │         ├─ 後から来たリクエストが先を上書きしていないか？
  │         │    ├─ YES → NG ❌ 先のアニメーションが消える
  │         │    └─ NO → OK
  │         │
  │         └─ 正しい方法:
  │              1. Queue<T> をフィールドに事前確保する（1回だけ new）
  │              2. 再生中でなければ即開始、再生中ならキューに入れる
  │              3. 完了時に Dequeue して次を開始する
  │              4. キューが空なら _animating = false にする
```

---

## 9. CanvasGroup と Raycast の罠

### 判断フロー

```
CanvasGroup を使ったオブジェクトの子要素がクリックに反応しない
  │
  ├─ 子要素の Image.raycastTarget は true か？
  │    ├─ NO → true にする
  │    └─ YES → 親の CanvasGroup を確認
  │
  ├─ 親の CanvasGroup.blocksRaycasts は true か？
  │    ├─ NO → ここが原因 ❌ true にする
  │    └─ YES → OK
  │
  ├─ 親の CanvasGroup.interactable は true か？
  │    ├─ NO → ここも原因の可能性 ❌ true にする
  │    └─ YES → OK
  │
  └─ いつ true に切り替えるか？
       ├─ アニメーション中に切り替える → NG（フェード中にクリックが反応してしまう）
       └─ アニメーション完了コールバック内で切り替える → OK ✅

  よくあるパターン:
    Awake: blocksRaycasts = false, interactable = false  ← キャリブ中はクリック不可
    OnFadeComplete: blocksRaycasts = true, interactable = true  ← ライブになったらクリック可
```

---

## 最終チェックリスト（コード生成・修正時に必ず通すこと）

コードを出力する前に、**上から順に 1 つずつ** 確認する。1 つでも NG があれば修正してから出力する。

```
□ 1. Update/FixedUpdate/LateUpdate 内でヒープアロケーションが発生していないか
     → 不安なら上の §1 の判断フローをもう一度たどる

□ 2. Debug.Log が #if UNITY_EDITOR または [Conditional] で保護されているか
     → 保護されていない Debug.Log を1つでも見つけたら即修正

□ 3. using System.Threading.Tasks; が含まれていないか
     → 含まれていたら削除して using Cysharp.Threading.Tasks; に置き換え

□ 4. すべての async メソッドが CancellationToken を受け取っているか
     → Unity イベント関数 (Start 等) は除外してよい

□ 5. クラスが namespace ToioLabs.XXX { } で囲まれているか
     → 囲まれていなかったらフォルダ構造に対応した名前空間を追加

□ 6. Inspector 変数が [SerializeField] private になっているか
     → public フィールドを1つでも見つけたら即修正

□ 7. private フィールドが _camelCase で命名されているか
     → アンダースコアなしを見つけたら即リネーム

□ 8. Time.time % N パターンが使われていないか
     → 見つけたら次回時刻キャッシュ方式に書き換え

□ 9. 重い処理が Update() から分離されているか
     → インラインで書かれていたらメソッドに切り出す

□ 10. ランタイム生成オブジェクトへの参照を保持する場合、
      Reset/Destroy 時の null チェックと null 代入があるか
      → なかったら追加

□ 11. CanvasGroup の interactable / blocksRaycasts の
      初期状態と遷移後の状態が正しいか
      → §9 の判断フローを確認

□ 12. SetParent() でランタイムに親を変更する場合、
      元の親を保存して復帰できるようになっているか
      → §7 の判断フローを確認
```
