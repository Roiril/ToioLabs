# UnityMCP 活用ルール（常時適用）

このプロジェクトには **UnityMCP** が導入されており、AIエージェントは MCP ツールを通じて Unity エディタと直接通信できる。このルールは MCP ツールの使用に関する **絶対的な行動規範** を定義する。

> **このファイルの使い方:** ユーザーからの依頼を受け取ったら、まず §1 から順に読み、該当するフローに従って行動する。

---

## 1. 推測の禁止 — 必ず事実を確認してから回答すること

### 判断フロー: 何かについて発言しようとしている時

```
シーン構成、アセットの存在、スクリプトの内容、コンポーネントの設定値
について何か発言しようとしている
  │
  ├─ その情報を MCP ツールで確認したか？
  │    ├─ YES → 確認結果に基づいて発言してよい ✅
  │    └─ NO → 発言してはならない ❌ まず以下のツールで確認する
  │
  │    何を確認したいか？
  │    ├─ シーン内の GameObject 構成 → manage_scene (action: get_hierarchy)
  │    ├─ 特定の GameObject の存在 → find_gameobjects (by_name / by_tag / by_component)
  │    ├─ コンポーネントの詳細 → read_resource: mcpforunity://scene/gameobject/{id}/components
  │    ├─ アセットの存在 → manage_asset (action: search)
  │    ├─ C# スクリプトの内容 → manage_script (action: read) または find_in_file
  │    ├─ コンソールログ → read_console
  │    └─ エディタの状態 → manage_editor (action: telemetry_status)
  │
  └─ 確認完了 → 結果に基づいて発言する ✅
```

### 絶対に禁止

- 「おそらく〇〇が原因です」と MCP 確認なしに推測すること
- 「そのスクリプトの内容を見せてください」とユーザーに聞き返すこと（MCP で読めるため）
- 「そのオブジェクトはシーンにありますか？」とユーザーに聞き返すこと（MCP で検索できるため）

### ユーザーに聞いてよい唯一のケース

- ユーザーの **意図** や **仕様要件** が不明瞭な場合（「どのような動作を期待していますか？」）
- MCP では取得できない情報（ハードウェアの状態、ネットワーク環境など）

---

## 2. コンテキストの自律的取得 — 聞き返す前に自分で調べること

### 判断フロー: ユーザーから報告を受けた時

```
ユーザーから何か報告を受けた
  │
  ├─ 「エラーが出た」「赤いログが出た」
  │    └─ 即座に実行: read_console (types: "error,exception", include_stacktrace: true)
  │
  ├─ 「〇〇が動かない」「挙動がおかしい」
  │    └─ 即座に実行:
  │         1. read_console でログ確認
  │         2. find_gameobjects で対象の存在確認
  │         3. read_resource でコンポーネント状態確認
  │
  ├─ 「〇〇のスクリプトを修正して」
  │    └─ 即座に実行:
  │         1. manage_script (action: read) で現在の内容を読む
  │         2. find_in_file で修正対象箇所を特定
  │
  ├─ 「NullReferenceException が出た」
  │    └─ 即座に実行:
  │         1. read_console (include_stacktrace: true)
  │         2. スタックトレースからファイル名・行番号を抽出
  │         3. manage_script (action: read) で該当ファイルを読む
  │
  └─ 「シーンに〇〇を追加して」
       └─ 即座に実行:
            1. manage_scene (action: get_hierarchy) で現在のシーン構成を確認
            2. それから作業を開始する
```

---

## 3. コード修正の完全手順

### 判断フロー: コードを修正する時

```
コードを修正する必要がある
  │
  ├─ Step 1: ファイルの特定
  │    └─ manage_asset (action: search) や find_in_file でパスを確認
  │       ❌ パスを推測して書いてはならない
  │
  ├─ Step 2: 現在の内容を読む
  │    └─ manage_script (action: read) で最新の内容を取得
  │       ❌ 過去の記憶やキャッシュに頼ってはならない
  │
  ├─ Step 3: 修正箇所を特定
  │    └─ find_in_file で対象パターンを検索、行番号を把握
  │
  ├─ Step 4: 修正方法を選ぶ（上から順に検討する）
  │    ├─ メソッド単位の置換 → script_apply_edits (op: replace_method) ← 最も安全
  │    ├─ パターンベースの挿入/置換 → script_apply_edits (op: anchor_insert / anchor_replace)
  │    ├─ 行指定の編集 → apply_text_edits（事前に find_in_file で位置を確認すること）
  │    └─ 新規ファイル作成 → create_script
  │
  ├─ Step 5: 構文検証
  │    └─ validate_script (level: standard, include_diagnostics: true)
  │       エラーがあれば修正して再保存。エラー 0 になるまで繰り返す
  │
  └─ Step 6: コンパイル確認
       └─ refresh_unity (compile: request, wait_for_ready: true)
          → read_console (types: error) で 0 件確認
          ❌ この確認なしに「完了」と報告してはならない
```

---

## 4. バッチ処理の活用

### 判断フロー: 複数の操作を行う時

```
複数の GameObject やコンポーネントに対して操作を行う
  │
  ├─ 操作は 2 つ以上あるか？
  │    ├─ YES → batch_execute を使う ✅
  │    │    例: 5つの Cube 作成 → 1 回の batch_execute に 5 つの create をまとめる
  │    └─ NO → 個別ツール呼び出しでよい
  │
  └─ 注意: batch_execute の commands 配列の各要素は
       {"tool": "ツール名", "params": {パラメータ}} の形式
```

---

## 5. スクリーンショットの活用

### 判断フロー: いつスクリーンショットを撮るか

```
以下のどれかに該当するか？
  │
  ├─ UI レイアウトを変更した → 撮る
  ├─ シーンにオブジェクトを配置した → 撮る
  ├─ ユーザーに結果を報告する → 撮る
  ├─ デバッグで視覚的確認が必要 → 撮る
  └─ どれにも該当しない → 撮らなくてよい
```

---

## 6. set_property でのオブジェクト参照設定

### 判断フロー: SerializeField にオブジェクト参照を設定する時

```
[SerializeField] にオブジェクト参照を設定したい
  │
  ├─ Step 1: 参照先の instanceID を取得する
  │    └─ find_gameobjects または get_hierarchy で instanceID を取得
  │       ❌ 過去の会話の instanceID を使い回してはならない（セッションごとに変わる）
  │
  ├─ Step 2: set_property で設定する
  │    └─ value に {"instanceID": <取得したID>} を渡す
  │
  └─ 例:
       manage_components
         action: "set_property"
         target: "RecalibrateButton"
         component_type: "RecalibrateButton"
         property: "_controller"
         value: {"instanceID": 28868}
```

---

## 7. localScale の正規化

### 判断フロー: 子要素を動的生成する前

```
あるオブジェクトの子に動的に要素を生成する予定がある
  │
  ├─ そのオブジェクトの localScale を get_hierarchy (include_transform: true) で確認したか？
  │    ├─ NO → まず確認する
  │    └─ YES → localScale は (1, 1, 1) か？
  │         ├─ YES → OK ✅ そのまま進む
  │         └─ NO → NG ❌ 座標系が歪む
  │              └─ manage_gameobject (action: modify, scale: [1,1,1]) で正規化する
  │                 ❌ sizeDelta でサイズ調整し、localScale は (1,1,1) を維持する
```

---

## 8. シーン保存のタイミング

### 判断フロー: いつシーンを保存するか

```
シーンに変更を加えている
  │
  ├─ すべての変更が完了したか？
  │    ├─ NO → まだ保存しない。途中で保存すると後続の操作が反映されないリスクがある
  │    └─ YES → 以下の順序で保存する
  │
  │    1. refresh_unity (compile: request, wait_for_ready: true)
  │    2. read_console (types: error) → 0 件か確認
  │       ├─ 0 件 → 次に進む
  │       └─ 1 件以上 → エラーを修正してからやり直す
  │    3. manage_scene (action: save) ← ここで初めて保存
  │    4. manage_scene (action: screenshot) ← 視覚的に確認
```
