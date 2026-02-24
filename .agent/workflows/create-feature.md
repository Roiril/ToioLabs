---
description: MCPツールを活用した新規機能・UIの自律的な作成手順
---

# /create-feature — 新規機能作成ワークフロー

ユーザーから新規機能の作成依頼を受けた時に、**何を考え、どの順番で何をすればよいか** を完全に決めたフロー。上から順に実行する。

> **前提:** `.agent/rules/unity-always-on-rules.md` と `.agent/rules/unity-mcp-guidelines.md` の全ルールに常に従うこと。

---

## 全体フロー図

```
ユーザーの依頼受信
    │
    ▼
Step 1: 現状確認（推測禁止、MCP で調べる）
    │
    ├─ 不明点あり？ → YES → ユーザーに確認（MCP で調べられないことだけ）
    │                  ╰─ 回答を受けたら Step 1 に戻る
    │
    └─ NO → Step 2
    ▼
Step 2: コード生成（ルール準拠チェック → ファイル保存 → 構文検証）
    │
    ├─ validate_script でエラー？ → YES → 修正して再保存 → 再検証
    │
    └─ NO → Step 3
    ▼
Step 3: シーン組み込み（GameObject 作成 → コンポーネント追加）
    │
    ▼
Step 4: 参照とパラメータ設定
    │
    ▼
Step 5: 検証と報告（コンパイル確認 → コンソールチェック → スクリーンショット → 保存 → 報告）
    │
    ├─ コンパイルエラー？ → YES → Step 2 に戻る
    ├─ ランタイムエラー？ → YES → /debug-unity に切り替え
    │
    └─ NO → ユーザーに報告して完了 ✅
```

---

## Step 1: 現状確認

### 思考フロー: 何を確認すればよいか

```
ユーザーの依頼を読んだ
  │
  ├─ Q1: 何を作るのか？ UI か、制御ロジックか、データ処理か？
  │    └─ これを決めないと名前空間もフォルダも決められない → 必ず最初に判断する
  │
  ├─ Q2: シーンの現在の構造を知っているか？
  │    ├─ NO → 実行: manage_scene (action: get_hierarchy, max_depth: 3, include_transform: true)
  │    └─ YES → 次に進む
  │
  ├─ Q3: UI 機能か？
  │    ├─ YES → Canvas と EventSystem を検索する
  │    │    └─ find_gameobjects (search_term: "Canvas", search_method: "by_component")
  │    │    └─ find_gameobjects (search_term: "EventSystem", search_method: "by_component")
  │    │    結果:
  │    │    ├─ Canvas がない → Step 3 で作る（フラグを立てておく）
  │    │    └─ EventSystem がない → Step 3 で作る（フラグを立てておく）
  │    └─ NO → 次に進む
  │
  ├─ Q4: 既存のスクリプトと重複しないか？
  │    └─ manage_asset (action: search, path: "Assets/Scripts", search_pattern: "*.cs")
  │       重複があればユーザーに報告する
  │
  └─ Q5: 以下を決定できたか？
       ├─ クラス名（PascalCase）
       ├─ 名前空間（ToioLabs.Control / ToioLabs.UI 等）
       ├─ フォルダパス（Assets/Scripts/Control/ 等）
       └─ 必要な GameObject とコンポーネントの一覧
       すべて決まった → Step 2 に進む
       決まっていない → MCP で調べるか、ユーザーに聞く
```

---

## Step 2: コード生成

### 思考フロー: コードを書く前のチェック

```
コードを書こうとしている
  │
  ├─ unity-always-on-rules.md の最終チェックリスト（12 項目）を
  │  頭の中に入れたか？
  │    ├─ NO → ファイルを開いてチェックリストを確認する
  │    └─ YES → 書き始めてよい
  │
  ├─ コードを書き終えた → 最終チェックリストを 1 項目ずつ通す
  │    ├─ 1 つでも NG → 修正する
  │    └─ すべて OK → ファイル保存に進む
  │
  ├─ ファイルを保存する
  │    └─ create_script (path: "Assets/Scripts/<フォルダ>/<クラス名>.cs", contents: ...)
  │
  └─ 構文を検証する
       └─ validate_script (uri: ..., level: "standard", include_diagnostics: true)
          ├─ エラー 0 → Step 3 に進む ✅
          └─ エラーあり → 修正して再保存 → 再検証（ループ）
```

---

## Step 3: シーンへの組み込み

### 思考フロー: 何を作ればよいか

```
シーンに組み込む必要がある
  │
  ├─ Step 1 で Canvas/EventSystem が不足していたか？
  │    ├─ YES → まず作る
  │    │    Canvas: manage_gameobject (create, components_to_add: ["Canvas", "CanvasScaler", "GraphicRaycaster"])
  │    │    EventSystem: manage_gameobject (create, components_to_add: ["EventSystem", "StandaloneInputModule"])
  │    │    Canvas の renderMode を設定: manage_components (set_property, property: "renderMode", value: 0)
  │    └─ NO → 次に進む
  │
  ├─ メインの GameObject を作成する
  │    └─ manage_gameobject (action: create, name: ..., parent: ...)
  │       UI の場合の parent → Canvas 名
  │       3D の場合の parent → なし、またはシーンルート
  │
  ├─ スクリプトをアタッチする
  │    └─ manage_components (action: add, target: ..., component_type: <クラス名>)
  │
  ├─ 追加コンポーネントを一括でアタッチ（2 つ以上なら batch_execute を使う）
  │
  └─ 子オブジェクトを作る（UI パーツなど）
       └─ manage_gameobject (action: create, parent: <親名>, ...)
```

---

## Step 4: パラメータと参照のセットアップ

### 思考フロー: 何を設定すればよいか

```
コンポーネントが追加された。パラメータを設定する。
  │
  ├─ Q1: RectTransform の位置調整が必要か？（UI の場合）
  │    ├─ YES → 複雑な Anchor 設定は避ける。人間が後で調整しやすい状態にする
  │    │    ❌ ピクセルパーフェクトを追求しない
  │    │    ✅ 画面中央など標準的な位置に仮配置する
  │    └─ NO → 次に進む
  │
  ├─ Q2: テキスト UI があるか？
  │    ├─ YES → 必ずテスト用の文字列を入れる。空のままにしない
  │    └─ NO → 次に進む
  │
  ├─ Q3: [SerializeField] のオブジェクト参照を設定する必要があるか？
  │    ├─ YES → unity-mcp-guidelines.md §6 のフローに従う
  │    │    1. find_gameobjects で参照先の instanceID を取得
  │    │    2. manage_components (set_property, value: {"instanceID": ...})
  │    │    ❌ 過去の会話の instanceID を使い回さない
  │    └─ NO → 次に進む
  │
  ├─ Q4: 親オブジェクトの localScale は (1,1,1) か？
  │    ├─ NO → unity-mcp-guidelines.md §7 のフローに従い正規化する
  │    └─ YES → OK
  │
  └─ Q5: 設定が正しく反映されたか確認する
       └─ read_resource (mcpforunity://scene/gameobject/<id>/components)
          null や None のフィールドがないか確認
```

---

## Step 5: 検証と報告

### 思考フロー: 何を確認すればよいか

```
実装が完了した。検証に入る。
  │
  ├─ 1. refresh_unity (compile: request, wait_for_ready: true)
  │
  ├─ 2. read_console (types: "error", count: "10")
  │    ├─ エラー 0 件 → 次に進む ✅
  │    ├─ コンパイルエラー → Step 2 に戻ってスクリプトを修正する
  │    └─ ランタイムエラー → /debug-unity ワークフローに切り替える
  │
  // turbo
  ├─ 3. manage_scene (action: screenshot, screenshot_file_name: "feature_result")
  │
  ├─ 4. manage_scene (action: save)
  │
  └─ 5. ユーザーへの報告（以下を含めること）
       ├─ 作成したスクリプト: ファイルパスとクラスの概要
       ├─ シーンの変更: 追加した GameObject とコンポーネントの一覧
       ├─ 設定した参照・値: Inspector で設定したパラメータの一覧
       ├─ スクリーンショット: 撮影した画像を添付
       └─ 動作確認手順: ユーザーが Play モードで確認すべき操作手順
```