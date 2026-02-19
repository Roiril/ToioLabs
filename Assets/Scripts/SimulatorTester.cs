using UnityEngine;
using toio; // CubeManager, Cube, CubeHandle, Movement のため
using Cysharp.Threading.Tasks; // UniTask (async/await) のために必要
using System.Threading; // CancellationTokenのために必要
using System; // OperationCanceledException のために必要

public class SimulatorTester : MonoBehaviour
{
    // --- 共通 ---
    private CubeManager simManager;
    private CubeHandle simHandle;
    private CubeManager realManager;
    private CubeHandle realHandle;

    // --- 状態管理 ---
    private bool isBusy = false; // 「くるくる」「しかく」の実行中フラグ
    private CancellationTokenSource cts; // 非同期タスクのキャンセル用

    // --- 時間管理 ---
    private float autoMoveTimer = 0f;
    private bool waveState = false; 
    private bool zigZagState = false;
    
    // --- ログ対策 ---
    private bool isStopping = true; 

    // =======================================================
    // --- Inspector（インスペクタ）設定項目 ---
    // =======================================================

    [Header("通常操作（WASD）")]
    [Tooltip("前進・後進のモーターパワー")]
    public int rcMoveSpeed = 80;
    [Tooltip("左回転・右回転のモーターパワー")]
    public int rcRotateSpeed = 60;
    [Tooltip("キー入力の反応間隔 (ミリ秒)")]
    public int rcDurationMs = 200;

    [Header("自動動作：押し続け")]
    [Tooltip("なみなみ動作時の前進パワー")]
    public int waveMoveSpeed = 70;
    [Tooltip("なみなみ動作時の回転パワー")]
    public int waveRotateSpeed = 70;
    [Tooltip("ギザギザ動作時の前後パワー")]
    public int zigZagMoveSpeed = 80;
    [Tooltip("ギザギザ動作時の回転パワー")]
    public int zigZagRotateSpeed = 100;
    [Tooltip("とんとん動作時の前進パワー")]
    public int tonTonMoveSpeed = 60;
    [Tooltip("押し続け動作中に全体がカーブする強さ (0=しない)")]
    public int autoCurveStrength = 10;

    [Header("自動動作：1回押し")]
    [Tooltip("【くるくる】円を描く時の前進パワー")]
    public int circleMoveSpeed = 60;
    [Tooltip("【くるくる】円を描く時の回転パワー")]
    public int circleRotateSpeed = 60;
    [Tooltip("【くるくる】一周する時間 (ミリ秒)")]
    public int circleDurationMs = 2000;
    [Tooltip("【しかく】一辺を移動するパワー")]
    public int squareMoveSpeed = 70;
    [Tooltip("【しかく】一辺を移動する時間 (ミリ秒)")]
    public int squareSideMs = 1000; 
    [Tooltip("【しかく】90度回転するパワー")]
    public int squareRotateSpeed = 100;
    [Tooltip("【しかく】90度回転する時間 (ミリ秒)")]
    public int squareTurnMs = 300;


    async void Start()
    {
        cts = new CancellationTokenSource();

        // (1) シミュレータへの接続
        Debug.Log("シミュレータに接続を開始...");
        simManager = new CubeManager(ConnectType.Simulator);
        Cube simCube = await simManager.SingleConnect();
        if (simCube != null && simManager.handles.Count > 0)
        {
            this.simHandle = simManager.handles[0];
            Debug.Log("シミュレータに接続成功！");
        }
        else { Debug.LogWarning("シミュレータに接続失敗。"); }

        // (2) 実機への接続
        Debug.Log("実機（BLE）のスキャンを開始...");
        realManager = new CubeManager(ConnectType.Real);
        Cube realCube = await realManager.SingleConnect();
        if (realCube != null && realManager.handles.Count > 0)
        {
            this.realHandle = realManager.handles[0];
            Debug.Log("実機に接続成功！");
        }
        else { Debug.LogWarning("実機に接続失敗。"); }
    }

    void OnDestroy()
    {
        cts?.Cancel(); 
        cts?.Dispose();
    }

    void Update()
    {
        // ハンドルのUpdateは常に呼ぶ
        simHandle?.Update();
        realHandle?.Update();

        // ----------------------------------------------------
        // --- 共通ラジコンロジック ---
        // ----------------------------------------------------

        if (isBusy) return;

        bool autoActionTriggered = false;

        // 2. 「1回押し」の動作 (GetKeyDown)
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
        {
            PerformCircle(); 
            return; 
        }

        if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
        {
            PerformSquare();
            return; 
        }

        // 3. 「押し続け」の動作 (GetKey)
        autoMoveTimer += Time.deltaTime;

        // 【なみなみ】 (キー: 3)
        if (Input.GetKey(KeyCode.Alpha3) || Input.GetKey(KeyCode.Keypad3))
        {
            if (autoMoveTimer > 0.15f)
            {
                waveState = !waveState;
                autoMoveTimer = 0f;
            }
            int rotate = waveState ? waveRotateSpeed : -waveRotateSpeed;
            MoveBothHandles(waveMoveSpeed, rotate + autoCurveStrength, 100);
            autoActionTriggered = true;
        }

        // 【ギザギザ】 (キー: 4)
        else if (Input.GetKey(KeyCode.Alpha4) || Input.GetKey(KeyCode.Keypad4))
        {
            if (autoMoveTimer > 0.2f)
            {
                zigZagState = !zigZagState;
                autoMoveTimer = 0f;
            }
            int move = zigZagState ? zigZagMoveSpeed : -zigZagMoveSpeed;
            MoveBothHandles(move, zigZagRotateSpeed + autoCurveStrength, 150);
            autoActionTriggered = true;
        }

        // 【とんとん】 (キー: 5)
        else if (Input.GetKey(KeyCode.Alpha5) || Input.GetKey(KeyCode.Keypad5))
        {
            if (autoMoveTimer > 0.15f)
            {
                autoMoveTimer = 0f;
            }
            int move = (autoMoveTimer < 0.08f) ? tonTonMoveSpeed : 0;
            MoveBothHandles(move, autoCurveStrength, 100);
            autoActionTriggered = true;
        }


        // 4. 通常のWASDキー操作（自動動作キーが押されていない時）
        if (autoActionTriggered)
        {
            isStopping = false; 
            return; 
        }

        float rotateInput = Input.GetAxisRaw("Horizontal"); 
        float moveInput = Input.GetAxisRaw("Vertical");     

        int finalMove = (int)(moveInput * rcMoveSpeed);
        int finalRotate = (int)(-rotateInput * rcRotateSpeed);

        if (finalMove != 0 || finalRotate != 0)
        {
            MoveBothHandles(finalMove, finalRotate, rcDurationMs);
            isStopping = false; 
        }
        else
        {
            if (!isStopping) 
            {
                MoveBothHandles(0, 0, 100); 
                isStopping = true; 
            }
        }
    }

    /**
     * 両方のハンドル（シミュレータと実機）に同じモーター命令を送る
     */
    void MoveBothHandles(int move, int rotate, int durationMs, bool border = false)
    {
        // シミュレータへの命令
        if (simHandle != null && simManager.IsControllable(simHandle.cube))
        {
            simHandle.Move(move, rotate, durationMs, border);
        }
        // 実機への命令
        if (realHandle != null && realManager.IsControllable(realHandle.cube))
        {
            realHandle.Move(move, rotate, durationMs, border);
        }
    }

    /**
     * 【くるくる】(円を描く)
     */
    async void PerformCircle() 
    {
        if (isBusy) return; 
        isBusy = true; 

        Debug.Log("くるくる（円） 開始！ (キー: 1)");
        MoveBothHandles(circleMoveSpeed, circleRotateSpeed, circleDurationMs);

        await UniTask.Delay(circleDurationMs, cancellationToken: cts.Token);
        
        MoveBothHandles(0, 0, 100); 
        Debug.Log("くるくる（円） 終了");
        isBusy = false; 
    }

    /**
     * 【しかく】
     */
    async void PerformSquare()
    {
        if (isBusy) return;
        isBusy = true;
        Debug.Log("しかく 開始！ (キー: 2)");

        try
        {
            for (int i = 0; i < 4; i++) // 4つの辺
            {
                // 1. 前進
                MoveBothHandles(squareMoveSpeed, 0, squareSideMs);
                await UniTask.Delay(squareSideMs, cancellationToken: cts.Token);

                // 2. 90度回転 (右回転)
                MoveBothHandles(0, -squareRotateSpeed, squareTurnMs);
                await UniTask.Delay(squareTurnMs, cancellationToken: cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Debug.Log("しかく 動作がキャンセルされました");
        }
        finally
        {
            MoveBothHandles(0, 0, 100); 
            Debug.Log("しかく 終了");
            isBusy = false;
        }
    }
}