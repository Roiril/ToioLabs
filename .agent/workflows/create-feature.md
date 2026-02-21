---
description: MCPツールを活用した新規機能・UIの自律的な作成手順
---

# /create-feature — 新規機能作成ワークフロー

ユーザーから新規機能の作成依頼を受けた際に、MCPツールを使用してスクリプト作成からシーン組み込み・検証までを一気通貫で完了させる手順。

> **前提:** `.agent/rules/unity-always-on-rules.md` と `.agent/rules/unity-mcp-guidelines.md` の全ルールに常に従うこと。

---

## Step 1: 要件定義と現状確認

ユーザーの要望を分析し、作業開始前にシーンと既存アセットの現状を MCP ツールで確認する。**推測で作業を開始してはならない。**

### 1-1. アクティブシーンの構造を確認する

```
ツール: manage_scene
パラメータ:
  action: "get_hierarchy"
  max_depth: 3
  include_transform: true
```

以下の有無を記録すること：
- `Canvas` (UI 構築に必要)
- `EventSystem` (UI インタラクションに必要)
- toio 関連の既存 GameObject (`CubeManager` をアタッチしたオブジェクト等)
- `Main Camera`, `Directional Light` 等の基本オブジェクト

### 1-2. UI 機能の場合 — Canvas と EventSystem の存在を検索する

```
ツール: find_gameobjects
パラメータ:
  search_term: "Canvas"
  search_method: "by_component"
```

```
ツール: find_gameobjects
パラメータ:
  search_term: "EventSystem"
  search_method: "by_component"
```

- Canvas が存在しない場合 → Step 3 で新規作成する（フラグを立てる）
- EventSystem が存在しない場合 → Step 3 で新規作成する（フラグを立てる）

### 1-3. toio 制御機能の場合 — 既存のコントローラーを確認する

```
ツール: manage_asset
パラメータ:
  action: "search"
  path: "Assets/Scripts"
  search_pattern: "*.cs"
```

- 既存の toio 制御スクリプトの接続パターンやコールバック登録方式を確認し、新規スクリプトで踏襲する。
- 重複する機能がないか確認する。重複する場合はユーザーに報告して判断を仰ぐ。

### 1-4. 要件の整理と確認

以下を決定してから次のステップに進むこと：
- **作成するスクリプトのクラス名** (PascalCase)
- **名前空間** (`ToioLabs.Control`, `ToioLabs.UI` 等)
- **保存先フォルダパス** (`Assets/Scripts/Control/`, `Assets/Scripts/UI/` 等)
- **必要な GameObject とコンポーネントの一覧**

不明な点がある場合のみユーザーに確認する。MCP で調べられる情報は聞き返さない。

---

## Step 2: ルールに準拠したスクリプト生成

`unity-always-on-rules.md` に完全準拠した C# コードを生成し、MCP ツールでファイルとして保存する。

### 2-1. コードを生成する

以下の全項目を満たすコードを生成すること。1つでも違反があれば修正してから保存する。

- [ ] `namespace ToioLabs.XXX { }` で囲まれている
- [ ] `Update` / `FixedUpdate` / `LateUpdate` 内でヒープアロケーションが発生していない
- [ ] `Debug.Log` が `#if UNITY_EDITOR` または `[System.Diagnostics.Conditional("UNITY_EDITOR")]` で保護されている
- [ ] `using System.Threading.Tasks;` が含まれていない（UniTask を使用）
- [ ] すべての非同期メソッド（Unity イベント関数を除く）が `async UniTask` で `CancellationToken` を受け取っている
- [ ] Inspector 変数が `[SerializeField] private` + `[Header]` + `[Tooltip]` になっている
- [ ] private フィールドが `_camelCase` で命名されている
- [ ] `Time.time % N` パターンが使われていない（次回時刻キャッシュ方式を使用）
- [ ] 重い処理が `Update()` から分離されている

### 2-2. ファイルとして保存する

```
ツール: create_script
パラメータ:
  path: "Assets/Scripts/<サブフォルダ>/<クラス名>.cs"
  contents: "<生成したC#コード>"
```

### 2-3. 構文を検証する

```
ツール: validate_script
パラメータ:
  uri: "Assets/Scripts/<サブフォルダ>/<クラス名>.cs"
  level: "standard"
  include_diagnostics: true
```

- エラーがある場合は修正して再保存する。エラーが解消するまで繰り返す。

---

## Step 3: シーンへの組み込み

MCP ツールを使用して、必要な GameObject の作成とコンポーネントのアタッチを行う。

### 3-1. 前提条件の確認と準備（UI 機能の場合）

Step 1 で Canvas / EventSystem が不足していた場合、先に作成する。

```
ツール: manage_gameobject
パラメータ:
  action: "create"
  name: "Canvas"
  components_to_add: ["Canvas", "CanvasScaler", "GraphicRaycaster"]
```

```
ツール: manage_gameobject
パラメータ:
  action: "create"
  name: "EventSystem"
  components_to_add: ["EventSystem", "StandaloneInputModule"]
```

Canvas のコンポーネント設定:
```
ツール: manage_components
パラメータ:
  action: "set_property"
  target: "Canvas"
  component_type: "Canvas"
  property: "renderMode"
  value: 0
```

```
ツール: manage_components
パラメータ:
  action: "set_property"
  target: "Canvas"
  component_type: "CanvasScaler"
  property: "uiScaleMode"
  value: 1
```

### 3-2. メインの GameObject を作成する

```
ツール: manage_gameobject
パラメータ:
  action: "create"
  name: "<機能名に対応する名前>"
  parent: "<親オブジェクト名 (UI の場合は Canvas)>"
  position: [0, 0, 0]
```

### 3-3. スクリプトをアタッチする

```
ツール: manage_components
パラメータ:
  action: "add"
  target: "<作成した GameObject の名前>"
  component_type: "<Step 2 で作成したクラス名>"
```

### 3-4. 追加コンポーネントを一括アタッチする（必要な場合）

複数のコンポーネントを追加する場合は `batch_execute` を使用すること。

```
ツール: batch_execute
パラメータ:
  commands:
    - tool: "manage_components"
      params:
        action: "add"
        target: "<GameObject名>"
        component_type: "TextMeshProUGUI"
    - tool: "manage_components"
      params:
        action: "add"
        target: "<GameObject名>"
        component_type: "Image"
```

### 3-5. 子オブジェクトを作成する（UI の場合）

UI パーツ（ボタン、テキスト、スライダー等）が必要な場合は子 GameObject として作成する。

```
ツール: manage_gameobject
パラメータ:
  action: "create"
  name: "<パーツ名 (例: TitleText)>"
  parent: "<親 GameObject 名>"
  components_to_add: ["RectTransform"]
```

---

## Step 4: パラメータと参照のセットアップ

MCP ツールを使用して、Inspector 上の値と参照を設定する。
**AIはピクセルパーフェクトなデザインを追求してはならない。** デザインの最終調整は人間が行うため、AIは「人間が後から微調整しやすい、プレーンで論理的なUI構造」を構築することに専念する。

### 4-1. RectTransform の仮配置（UI の場合）
複雑なAnchor設定や極端なサイズ指定は避け、後から人間が自由に動かせる標準的な状態（画面中央など）に仮配置するにとどめる。
人間が視覚的にレイアウトを調整しやすいように、テキストUIには**必ずテスト用の文字列を挿入し、空っぽの状態にしないこと。

```
ツール: manage_components
パラメータ:
  action: "set_property"
  target: "<GameObject名>"
  component_type: "RectTransform"
  property: "anchorMin"
  value: {"x": 0, "y": 0}
```

```
ツール: manage_components
パラメータ:
  action: "set_property"
  target: "<GameObject名>"
  component_type: "RectTransform"
  property: "anchorMax"
  value: {"x": 1, "y": 1}
```

```
ツール: manage_components
パラメータ:
  action: "set_property"
  target: "<GameObject名>"
  component_type: "RectTransform"
  property: "sizeDelta"
  value: {"x": 0, "y": 0}
```

### 4-2. SerializeField への参照割り当て

スクリプトの `[SerializeField]` フィールドに参照を設定する。

まず、設定対象のフィールド一覧をコンポーネント情報から確認する：

```
ツール: read_resource
パラメータ:
  ServerName: "unityMCP"
  Uri: "mcpforunity://scene/gameobject/<instance_id>/component/<クラス名>"
```

次に、各フィールドに値を設定する：

```
ツール: manage_components
パラメータ:
  action: "set_property"
  target: "<GameObject名>"
  component_type: "<クラス名>"
  property: "<フィールド名>"
  value: <設定値>
```

### 4-3. 設定結果の確認

設定が正しく反映されたか確認する：

```
ツール: read_resource
パラメータ:
  ServerName: "unityMCP"
  Uri: "mcpforunity://scene/gameobject/<instance_id>/components"
```

- `null` や `None` のまま残っているフィールドがないか確認する。
- 想定と異なる値が設定されていないか確認する。

---

## Step 5: 動作検証と視覚的報告

実装が完了したら、コンパイルエラーの確認とスクリーンショットによる視覚的報告を行う。

### 5-1. アセットのリフレッシュとコンパイル確認

```
ツール: refresh_unity
パラメータ:
  compile: "request"
  wait_for_ready: true
```

### 5-2. コンソールエラーの確認

```
ツール: read_console
パラメータ:
  types: "error"
  count: "10"
```

- コンパイルエラーがある場合 → Step 2 に戻りスクリプトを修正する。
- ランタイムエラーがある場合 → `/debug-unity` ワークフローに切り替える。
- エラーがない場合 → 次に進む。

### 5-3. スクリーンショットを撮影する

// turbo
```
ツール: manage_scene
パラメータ:
  action: "screenshot"
  screenshot_file_name: "feature_result"
```

### 5-4. シーンを保存する

```
ツール: manage_scene
パラメータ:
  action: "save"
```

### 5-5. ユーザーへの報告

以下を含む報告を行うこと：

1. **作成したスクリプト:** ファイルパスとクラスの概要
2. **シーンの変更:** 追加した GameObject とコンポーネントの一覧
3. **設定した参照・値:** Inspector で設定したパラメータの一覧
4. **スクリーンショット:** 撮影した画像を添付
5. **動作確認手順:** ユーザーが Play モードで確認すべき操作手順

---

## フローチャート（全体の流れ）

```
ユーザーの依頼受信
    │
    ▼
Step 1: 要件定義 + 現状確認 (MCP でシーン・アセットを調査)
    │
    ├─ 不明点あり → ユーザーに確認
    │
    ▼
Step 2: スクリプト生成 (create_script → validate_script)
    │
    ├─ 検証エラー → 修正して再保存
    │
    ▼
Step 3: シーン組み込み (manage_gameobject → manage_components)
    │
    ▼
Step 4: パラメータ設定 (manage_components set_property)
    │
    ├─ 参照が null → 再設定
    │
    ▼
Step 5: 検証 + 報告 (refresh_unity → read_console → screenshot)
    │
    ├─ コンパイルエラー → Step 2 に戻る
    ├─ ランタイムエラー → /debug-unity に切り替え
    │
    ▼
完了 → ユーザーに報告
```