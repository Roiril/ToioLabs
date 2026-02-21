---
description: UnityMCPを活用したエラー・バグの自律的デバッグ手順
---

# /debug-unity — Unity デバッグワークフロー

Unity エディタでエラーやバグが発生した際に、MCP ツールを使って自律的に原因を特定し修正するためのステップバイステップ手順。

---

## Step 1: コンソールログの取得

最新のエラーログとスタックトレースを取得する。

```
ツール: read_console
パラメータ:
  types: "error,exception"
  count: "10"
  include_stacktrace: true
```

- エラーがない場合は `types` を `"warning,error,exception"` に広げて再取得する。
- ユーザーが特定のキーワードに言及している場合は `filter_text` を指定する。
- 取得したログから以下を抽出して記録する：
  - **エラーメッセージ全文**
  - **例外の型**（`NullReferenceException`, `MissingReferenceException` 等）
  - **スタックトレースのファイル名と行番号**

---

## Step 2: 原因箇所の特定とソースコード読み込み

Step 1 で特定したファイル名・行番号をもとに、該当するソースコードを読み込む。

```
ツール: manage_script
パラメータ:
  action: "read"
  name: "<エラーのスタックトレースに含まれるスクリプト名>"
  path: "<Assets/ 以下のパス>"
```

- ファイルパスが不明な場合は `manage_asset` (action: `search`, search_pattern: `"<スクリプト名>.cs"`) で検索する。
- 読み込んだコードのエラー該当行とその前後 20 行を重点的に確認する。
- `find_in_file` を使って、null の可能性がある変数名やメソッド呼び出しを検索する。

### NullReferenceException の場合の追加調査

エラーが `NullReferenceException` の場合、以下を重点的に確認する：

1. **Inspector 参照の未アサイン:** `[SerializeField]` フィールドが Inspector で設定されていない可能性
   → Step 3 で該当 GameObject のコンポーネント状態を確認する
2. **初期化タイミング:** `Awake()` / `Start()` の実行順序の問題
   → `GetComponent` や `Find` が null を返していないか確認する
3. **非同期処理での破棄:** `await` 後に `this` や GameObject が破棄されている可能性
   → `CancellationToken` の使用状況を確認する

---

## Step 3: シーンの状況確認

必要に応じて、アクティブシーンの Hierarchy と該当 GameObject の状態を確認する。

### 3-1. シーン全体の構造を確認

```
ツール: manage_scene
パラメータ:
  action: "get_hierarchy"
  max_depth: 3
```

### 3-2. 該当 GameObject を検索

```
ツール: find_gameobjects
パラメータ:
  search_term: "<エラーに関連する GameObject 名またはコンポーネント名>"
  search_method: "by_name"  (または "by_component")
```

### 3-3. コンポーネントの詳細確認

```
ツール: read_resource
パラメータ:
  ServerName: "unityMCP"
  Uri: "mcpforunity://scene/gameobject/<instance_id>/components"
```

- Inspector で設定されるべきフィールドが `null` や `None` になっていないか確認する。
- コンポーネントが正しくアタッチされているか確認する。
- `isActiveAndEnabled` が `false` のオブジェクトがないか確認する。

---

## Step 4: 原因分析と修正案の提示

収集した情報をもとに原因を分析し、修正コードを提示する。

### 修正コードの生成ルール

修正コードは `.agent/rules/unity-always-on-rules.md` の全ルールに従うこと。特に以下を確認：

- [ ] `Update` 系メソッド内でヒープアロケーションが発生していないか
- [ ] `async` 処理が UniTask で統一されているか、`CancellationToken` を渡しているか
- [ ] 名前空間 `namespace ToioLabs.XXX` で囲まれているか
- [ ] Inspector 変数が `[SerializeField] private` になっているか
- [ ] private フィールドが `_camelCase` で命名されているか

### 修正の適用

修正方法は以下の優先順位で選択する：

1. **メソッド単位の修正** → `script_apply_edits` (op: `replace_method`)
2. **パターンベースの挿入** → `script_apply_edits` (op: `anchor_insert`)
3. **行単位の修正** → `apply_text_edits`（事前に行番号を `find_in_file` で確認）
4. **ファイル全体の再作成** → `create_script`（大規模なリファクタリング時のみ）

---

## Step 5: 修正後の検証

修正を適用した後、以下の検証を行う。

### 5-1. スクリプトの構文検証

```
ツール: validate_script
パラメータ:
  uri: "<修正したスクリプトのパス>"
  level: "standard"
  include_diagnostics: true
```

### 5-2. アセットのリフレッシュとコンパイル確認

```
ツール: refresh_unity
パラメータ:
  compile: "request"
  wait_for_ready: true
```

### 5-3. コンパイルエラーの確認

```
ツール: read_console
パラメータ:
  types: "error"
  count: "5"
```

- コンパイルエラーが残っている場合は Step 2 に戻り、修正を繰り返す。
- エラーが解消されたら、ユーザーに以下を報告する：
  1. **原因の説明**（何が問題だったか）
  2. **修正内容の要約**（何を変更したか）
  3. **動作確認の依頼**（Play モードで確認すべき点）

---

## よくあるエラーパターンと対処法

| エラー | よくある原因 | 調査方法 |
|---|---|---|
| `NullReferenceException` | Inspector 未アサイン / 初期化順序 / await 後の破棄 | Step 3 で該当オブジェクトのコンポーネント状態を確認 |
| `MissingReferenceException` | Destroy 済みオブジェクトへの参照 | `find_in_file` で `Destroy` 呼び出し箇所を検索 |
| `SerializationException` | 名前空間変更によるシリアライズ破損 | スクリプトの namespace 変更履歴を確認 |
| BLE 接続エラー | Cube未接続 / Bluetooth OFF / 同時接続数超過 | `read_console` でエラーログ → `find_in_file` で接続処理を確認 |
| `TargetMove` 異常 | 座標範囲外 (0-65535) / 角度範囲外 (0-8191) | `find_in_file` で `TargetMove` 呼び出しのパラメータ値を確認 |
