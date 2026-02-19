using UnityEngine;
using UnityEngine.UI; // Slider用
using TMPro;          // TextMeshPro用
using toio;
using Cysharp.Threading.Tasks;
using System.Threading;

public class ZoetropeController : MonoBehaviour
{
    // --- toio接続関連 ---
    private CubeManager simManager;
    private CubeHandle simHandle;
    private CubeManager realManager;
    private CubeHandle realHandle;
    
    // --- 制御用 ---
    private CancellationTokenSource cts;
    
    [Header("回転設定")]
    [Tooltip("現在の回転速度 (-100 ~ 100)")]
    [Range(-100, 100)]
    public int rotationSpeed = 0; 
    
    [Tooltip("回転のON/OFF")]
    public bool isSpinning = false;

    [Header("UI参照")]
    [Tooltip("速度調整用スライダー (Legacy UI Slider)")]
    public Slider speedSlider;
    [Tooltip("現在の速度を表示するTextMeshPro")]
    public TextMeshProUGUI speedText; // TMPに変更

    [Header("ストロボ効果 (シミュレーション用)")]
    [Tooltip("点滅させるライト (Directional Lightなど)")]
    public Light strobeLight;
    [Tooltip("ストロボのON/OFF")]
    public bool enableStrobe = true;

    private float strobeTimer = 0f;

    async void Start()
    {
        cts = new CancellationTokenSource();

        // 【重要】Unityの環境光をコードから強制的に真っ暗にする（ストロボを見やすくするため）
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;

        // UIの初期化
        if (speedSlider != null)
        {
            speedSlider.minValue = -100;
            speedSlider.maxValue = 100;
            speedSlider.value = rotationSpeed;
            speedSlider.onValueChanged.AddListener(OnSliderChanged);
        }
        UpdateSpeedText();

        // --- toio接続処理 ---
        
        // (1) シミュレータ
        simManager = new CubeManager(ConnectType.Simulator);
        Cube simCube = await simManager.SingleConnect();
        if (simCube != null && simManager.handles.Count > 0)
        {
            this.simHandle = simManager.handles[0];
            Debug.Log("【Sim】接続成功");
        }

        // (2) 実機
        realManager = new CubeManager(ConnectType.Real);
        Cube realCube = await realManager.SingleConnect();
        if (realCube != null && realManager.handles.Count > 0)
        {
            this.realHandle = realManager.handles[0];
            Debug.Log("【Real】接続成功");
            
            // 実機のLEDを緑にして準備完了を知らせる
            realHandle.cube.TurnLedOn(0, 255, 0, 500);
        }
    }

    void OnDestroy()
    {
        cts?.Cancel();
        cts?.Dispose();
    }

    void Update()
    {
        // ハンドル更新
        simHandle?.Update();
        realHandle?.Update();

        // --- キーボード操作 (微調整用) ---
        if (Input.GetKeyDown(KeyCode.Space))
        {
            isSpinning = !isSpinning;
        }

        // 矢印キーで速度を微調整
        if (Input.GetKeyDown(KeyCode.RightArrow)) ChangeSpeed(5);
        if (Input.GetKeyDown(KeyCode.LeftArrow)) ChangeSpeed(-5);
        if (Input.GetKey(KeyCode.UpArrow)) ChangeSpeed(1); 
        if (Input.GetKey(KeyCode.DownArrow)) ChangeSpeed(-1);

        // --- toioへの命令送信 ---
        if (isSpinning && rotationSpeed != 0)
        {
            // その場で回転 (Moveの第一引数を0にする)
            MoveBothHandles(0, rotationSpeed, 200);
        }
        else
        {
            // 停止
            MoveBothHandles(0, 0, 200);
        }

        // --- ストロボ処理 (Unity上の見た目用) ---
        if (enableStrobe && strobeLight != null)
        {
            if (isSpinning && rotationSpeed != 0)
            {
                HandleStrobeLight();
            }
            else
            {
                // 回転していない時は常時点灯に戻す
                strobeLight.enabled = true;
            }
        }
    }

    // 両方のハンドルに命令を送るヘルパー関数
    void MoveBothHandles(int move, int rotate, int durationMs)
    {
        if (simHandle != null && simManager.IsControllable(simHandle.cube))
            simHandle.Move(move, rotate, durationMs, false);

        if (realHandle != null && realManager.IsControllable(realHandle.cube))
            realHandle.Move(move, rotate, durationMs, false);
    }

    // スライダーの値が変更された時に呼ばれる
    void OnSliderChanged(float val)
    {
        rotationSpeed = (int)val;
        UpdateSpeedText();
    }

    // 速度変更ヘルパー
    void ChangeSpeed(int delta)
    {
        rotationSpeed = Mathf.Clamp(rotationSpeed + delta, -100, 100);
        if (speedSlider != null) speedSlider.value = rotationSpeed;
        UpdateSpeedText();
    }

    void UpdateSpeedText()
    {
        if (speedText != null)
        {
            speedText.text = $"Speed: {rotationSpeed}";
        }
    }

    // ストロボ制御ロジック
    void HandleStrobeLight()
    {
        // 回転速度の絶対値を取得（0除算防止）
        float absSpeed = Mathf.Abs(rotationSpeed);
        if (absSpeed < 1) return;

        // 速度に応じて点滅間隔を変える
        // 係数(0.2f)を変えると点滅の速さが変わるので、見えやすい値に調整してください
        float interval = 1.0f / (absSpeed * 0.2f); 
        
        strobeTimer += Time.deltaTime;
        if (strobeTimer >= interval)
        {
            strobeLight.enabled = !strobeLight.enabled; // ON/OFF切り替え
            strobeTimer = 0f;
        }
    }
}