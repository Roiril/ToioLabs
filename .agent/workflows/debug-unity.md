---
description: UnityMCPを活用したエラー・バグの自律的デバッグ手順
---

# /debug-unity — Unity デバッグワークフロー

Unity エディタでエラーやバグが発生した際に、**何を考え、どの順番で何をすればよいか** を完全に決めたフロー。上から順に実行する。

---

## 全体フロー図

```
エラーやバグの報告を受けた
    │
    ▼
Step 1: ログ取得（何が起きているかをまず事実で把握する）
    │
    ├─ エラーが見つかった？
    │    ├─ YES → Step 2 に進む
    │    └─ NO → types を広げて再取得、または filter_text でキーワード検索
    │
    ▼
Step 2: 原因箇所の特定（スタックトレースからファイルと行番号を読む）
    │
    ▼
Step 3: シーンの状態確認（Inspector 値、コンポーネント、オブジェクトの有無）
    │
    ▼
Step 4: 原因分析と修正コードの生成
    │
    ├─ unity-always-on-rules.md のチェックリストを通す
    │
    ▼
Step 5: 修正の適用と検証
    │
    ├─ validate_script → refresh_unity → read_console
    │
    ├─ エラー残り？ → YES → Step 2 に戻る
    │
    └─ NO → ユーザーに報告して完了 ✅
```

---

## Step 1: コンソールログの取得

### 思考フロー

```
エラーやバグの報告を受けた
  │
  ├─ まずログを取得する（推測しない）
  │    └─ read_console (types: "error,exception", count: "10", include_stacktrace: true)
  │
  ├─ ログが 0 件だったか？
  │    ├─ YES → types を広げる
  │    │    └─ read_console (types: "warning,error,exception", count: "20")
  │    │    それでも 0 件なら → ユーザーが言及したキーワードで filter_text 検索
  │    └─ NO → ログを分析する
  │
  └─ ログから以下を抽出して記録する（Step 2 で使う）
       ├─ エラーメッセージ全文
       ├─ 例外の型（NullReferenceException, MissingReferenceException 等）
       └─ スタックトレースのファイル名と行番号
```

---

## Step 2: 原因箇所の特定

### 思考フロー: エラーの種類ごとに何をするかが違う

```
Step 1 でエラーの種類がわかった
  │
  ├─ NullReferenceException の場合
  │    │
  │    ├─ スタックトレースにファイル名と行番号があるか？
  │    │    ├─ YES → manage_script (action: read) でそのファイルを読む
  │    │    │    → find_in_file でエラー行の前後を確認
  │    │    └─ NO → find_in_file で例外メッセージのキーワードを検索
  │    │
  │    └─ 原因は以下の 3 つのどれかに絞り込む:
  │         ├─ A: Inspector 参照の未アサイン
  │         │    → Step 3 でコンポーネント状態を確認する
  │         ├─ B: 初期化タイミングの問題（Awake/Start の実行順序）
  │         │    → GetComponent / Find が null を返していないか確認
  │         └─ C: async await 後にオブジェクトが Destroy されている
  │              → CancellationToken の使用状況を確認
  │
  ├─ MissingReferenceException の場合
  │    │
  │    ├─ 原因: Destroy 済みオブジェクトへの参照が残っている
  │    ├─ find_in_file で Destroy の呼び出し箇所を検索
  │    └─ Destroy した後にフィールドを null にしているか確認
  │         ├─ していない → NG ❌ null 代入を追加する
  │         └─ している → 参照しているフィールドが別の箇所にないか検索
  │
  ├─ UI クリックが反応しない場合
  │    │
  │    ├─ 原因候補を上から順に確認する:
  │    │    1. CanvasGroup.blocksRaycasts が false → コンポーネントリソースで確認
  │    │    2. CanvasGroup.interactable が false → 同上
  │    │    3. Image.raycastTarget が false → 同上
  │    │    4. 親の CanvasGroup が子の raycast を遮断している → 親を遡って確認
  │    │    5. IPointerDownHandler がコントローラー本体にあり、
  │    │       動的生成した要素では発火しない → §6 イベント委譲パターンに変更
  │    └─ 上から順に確認し、最初に見つかった原因を修正する
  │
  ├─ 座標系のずれ・歪みの場合
  │    │
  │    ├─ get_hierarchy (include_transform: true) で当該オブジェクトと親の scale を確認
  │    ├─ localScale が (1,1,1) でない → これが原因 ❌ 正規化する
  │    └─ localScale が (1,1,1) の場合 → pivot / anchor 設定を確認
  │
  ├─ アニメーションの途中消失の場合
  │    │
  │    ├─ find_in_file でアニメーション開始のフラグ管理を検索
  │    ├─ 複数リクエストが先行リクエストを上書きしていないか確認
  │    └─ 上書きしている → キューイング方式に変更（§8 参照）
  │
  ├─ BLE 接続エラーの場合
  │    │
  │    ├─ read_console でエラーログを確認
  │    └─ find_in_file で接続処理のコードを検索
  │
  ├─ TargetMove 異常の場合
  │    │
  │    ├─ find_in_file で TargetMove 呼び出しを検索
  │    ├─ 座標が 0–65535 の範囲内か確認
  │    └─ 角度が 0–8191 の範囲内か確認
  │
  └─ 上記に該当しない場合
       └─ エラーメッセージをそのまま検索キーワードにして find_in_file で関連コードを探す
```

---

## Step 3: シーンの状態確認

### 思考フロー: 何を確認するか

```
原因箇所は特定できた。シーン側の状態を確認する。
  │
  ├─ 1. シーン全体の構造を確認
  │    └─ manage_scene (action: get_hierarchy, max_depth: 3)
  │
  ├─ 2. 問題のある GameObject を検索
  │    └─ find_gameobjects (search_term: ..., search_method: by_name)
  │       見つからない？ → by_component で検索する
  │
  ├─ 3. コンポーネントの詳細を確認
  │    └─ read_resource (mcpforunity://scene/gameobject/<id>/components)
  │
  └─ 4. 以下を確認したか？
       ├─ [SerializeField] のフィールドが null / None になっていないか
       ├─ コンポーネントが正しくアタッチされているか
       ├─ isActiveAndEnabled が false のオブジェクトがないか
       └─ localScale が (1,1,1) か
```

---

## Step 4: 原因分析と修正コードの生成

### 思考フロー: 修正の前に

```
原因がわかった。修正コードを書く。
  │
  ├─ 修正コードを書く前に:
  │    └─ unity-always-on-rules.md の最終チェックリスト（12 項目）を
  │       頭に入れているか？
  │       ├─ NO → 開いて確認する
  │       └─ YES → 書き始めてよい
  │
  ├─ 修正方法を上から順に検討する（最も安全なものから選ぶ）:
  │    1. メソッド単位の修正 → script_apply_edits (op: replace_method) ← 最優先
  │    2. パターンベースの挿入 → script_apply_edits (op: anchor_insert)
  │    3. 行単位の修正 → apply_text_edits（事前に find_in_file で行番号を確認）
  │    4. ファイル全体の再作成 → create_script（大規模リファクタリング時のみ）
  │
  └─ 修正を書き終えた → 最終チェックリストを 1 項目ずつ通す
```

---

## Step 5: 修正後の検証

### 思考フロー: 検証手順を最後まで実行する

```
修正を適用した
  │
  ├─ 1. validate_script (uri: ..., level: standard, include_diagnostics: true)
  │    ├─ エラー 0 → 次に進む ✅
  │    └─ エラーあり → 修正して再保存 → 再検証（ループ）
  │
  ├─ 2. refresh_unity (compile: request, wait_for_ready: true)
  │
  ├─ 3. read_console (types: "error", count: "5")
  │    ├─ エラー 0 → 修正完了 ✅
  │    └─ エラーあり → Step 2 に戻って再分析
  │
  └─ 4. ユーザーに報告する（以下を含めること）
       ├─ 原因の説明（何が問題だったか）
       ├─ 修正内容の要約（何を変更したか）
       └─ 動作確認の依頼（Play モードで確認すべき点）
```

---

## よくあるエラーパターン早見表

問題が発生したら、まずこの表で原因の見当をつけてから Step 2 に進む。

| エラー | よくある原因 | 最初に実行するツール |
|:--|:--|:--|
| `NullReferenceException` | Inspector 未アサイン / 初期化順序 / await 後の破棄 | `read_console` → コンポーネントリソースで null フィールド確認 |
| `MissingReferenceException` | Destroy 済みオブジェクトへの参照 | `find_in_file` で `Destroy` 呼び出しを検索 |
| `SerializationException` | 名前空間変更によるシリアライズ破損 | スクリプトの namespace を確認 |
| UI クリック不反応 | CanvasGroup.blocksRaycasts=false / raycastTarget=false | コンポーネントリソースで CanvasGroup の値を確認 |
| 座標系のずれ・歪み | localScale が (1,1,1) でない / pivot 不一致 | `get_hierarchy` (include_transform: true) |
| アニメーション途中消失 | 同時発火で先行リクエスト上書き | `find_in_file` でフラグ管理箇所を検索 |
| Destroy 後の例外 | フィールドが Destroy 済みオブジェクトを参照 | `find_in_file` で Destroy と参照箇所を検索 |
| BLE 接続エラー | Cube未接続 / Bluetooth OFF / 同時接続数超過 | `read_console` → `find_in_file` で接続処理を確認 |
| `TargetMove` 異常 | 座標範囲外 / 角度範囲外 | `find_in_file` で `TargetMove` のパラメータを確認 |
