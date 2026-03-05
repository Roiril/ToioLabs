# toio SDK チートシート (AI向けリファレンス)

このドキュメントは、Unity上のtoio SDK (`toio-sdk`) において、AIがtoioコアキューブを制御・連携するための仕様とインターフェースをまとめたものです。
主に `toio.Cube` クラスのプロパティやメソッドを中心に記載しています。

## 1. toioキューブから取得できるパラメータ（プロパティ）
キューブ側から定期的に送られてくる、あるいは保持している状態です。

### 基本情報
- `version` (string): BLE プロトコルバージョン
- `id` (string): キューブの固有識別ID (MACアドレス等)
- `addr` (string): アドレス
- `localName` (string): BLEのローカルネーム
- `isConnected` (bool): 接続状態
- `battery` (int): バッテリー残量状態（目安）
- `maxSpd` (int): コアキューブの最高速度
- `deadzone` (int): モーター指令のデッドゾーン

### センサー情報（専用プレイマット・光学センサー）
- `x`, `y` (int): マット上のXY座標（絶対座標）
- `pos` (Vector2): マット上のXY座標 (UnityのVector2型表現)
- `angle` (int): マット上の角度
- `sensorPos` (Vector2), `sensorAngle` (int): 光学センサー自体の位置と角度
- `standardId` (uint): 読み取り可能な特殊ステッカーのID（カードやシールなど）

### 状態（ハードウェア・モーションセンサー等）
- `isPressed` (bool): ボタン押下状態
- `isSloped` (bool): 傾き状態
- `isCollisionDetected` (bool): 衝突検知状態
- `isGrounded` (bool): マットへの接地状態
- `isDoubleTap` (bool): ダブルタップ状態 (※v2.1.0以降)
- `pose` (PoseType): 姿勢状態（Up, Down, Front, Back, Right, Left） (※v2.1.0以降)
- `shakeLevel` (int): シェイク状態 (※v2.2.0以降)
- `magnetState` (MagnetState): 磁石の接近状態 (※v2.2.0以降)

### モーター・高度なセンサー類
- `leftSpeed`, `rightSpeed` (int): 左右モーターの実速度 (※v2.2.0以降)
- `magneticForce` (Vector3): 磁力ベクトル (※v2.3.0以降)
- `eulers` (Vector3), `quaternion` (Quaternion): キューブの姿勢角 (※v2.3.0以降)

---

## 2. イベント検知 (コールバック)
特定のイベントが発生したときに呼ばれるデリゲート群です。`Cube.CallbackProvider<Cube>` にアクションを登録して使用します。

- `buttonCallback`: ボタンが押された / 離されたとき
- `slopeCallback`: 傾きが変化したとき
- `collisionCallback`: 衝突を検知したとき
- `idCallback`: 座標や角度が更新されたとき（読み取り更新）
- `standardIdCallback`: 特殊ステッカーのIDを読み取ったとき
- `idMissedCallback`: 座標が読み取れなくなったとき（マット外など）
- `doubleTapCallback`: ダブルタップを検知したとき
- `poseCallback`: キューブの姿勢（面）が変わったとき
- `shakeCallback`: シェイクを検知したとき
- `motorSpeedCallback`: モーター速度情報が更新されたとき
- `magnetStateCallback`: 磁石状態が更新されたとき
- `magneticForceCallback`: 磁力が更新されたとき
- `attitudeCallback`: 姿勢角が更新されたとき

---

## 3. toioキューブへの指示（制御・コマンドメソッド）
キューブに対して行えるアクションです。
※引数には基本的に `ORDER_TYPE.Strong` / `Weak` の優先度指定が可能です。

### モーター制御 (移動)
- **直接制御**
  `Move(int left, int right, int durationMs)`
  左右のモーター速度と実行時間を直接指定します。
- **目標地点への移動**
  `TargetMove(int targetX, int targetY, int targetAngle, ...)`
  ターゲットの座標・角度を指定して移動させます。移動タイプ（前進のみ、回転してから移動など）も指定可能。
- **加速度指定移動**
  `AccelerationMove(int targetSpeed, int acceleration, int rotationSpeed, ...)`
  加速度や回転速度を指定して滑らかに移動させます。

### LED・サウンド制御
- **LED制御**
  - `TurnLedOn(int red, int green, int blue, int durationMs)`: LEDの点灯
  - `TurnLedOff()`: LEDの消灯
  - `TurnOnLightWithScenario(int repeatCount, LightOperation[] operations)`: LEDの連続発光シーケンス実行
- **サウンド制御**
  - `PlayPresetSound(int soundId, int volume)`: 組み込みのプリセット効果音を再生
  - `PlaySound(int repeatCount, SoundOperation[] operations)`: MIDIノート（任意の音符）の連続再生シーケンス
  - `StopSound()`: サウンドの強制停止

### 設定・リクエスト
一部の設定コマンドは `UniTask` による非同期実行に対応しています。

- **感度・閾値設定**
  `ConfigSlopeThreshold`, `ConfigCollisionThreshold`, `ConfigDoubleTapInterval` など。
- **センサーの有効化・通知設定**
  `ConfigMotorRead`, `ConfigIDNotification`, `ConfigMagneticSensor`, `ConfigAttitudeSensor` など。
- **オンデマンドリクエスト**
  `RequestMotionSensor`, `RequestMagneticSensor`, `RequestAttitudeSensor`: コールバックを待たずに即時センサー値をリクエストします。

---

## 4. 開発時のシステム構造
SDKを扱う上での主要クラス群です。生要素である `Cube` に対してラッパーやマネージャーが存在します。

- **`CubeManager`**
  - 用途: 複数キューブの接続（BLEスキャン）と一覧管理。
  - 主要要素: `SingleConnect()`, `MultiConnect()`, `cubes` リスト。
- **`CubeHandle`**
  - 用途: `Cube` に対する高レベルな移動・回転コマンドユーティリティ。より直感的なAPI（独自のPID制御を用いた補正移動や、指定距離の移動など）を提供。通常はこちらを介して移動を制御すると便利。
- **`CubeNavigator`**
  - 用途: 経路探索や障害物（他キューブ等）回避を行うためのクラス。自律移動時に使用。

**開発時のヒント:**
基本的な位置取得やコールバックのフックは `Cube` クラスから行い、アプリケーションのロジックとしてキューブを走らせる場合は `CubeManager` から取得した `CubeHandle` などの移動APIを活用すると開発がスムーズになります。
