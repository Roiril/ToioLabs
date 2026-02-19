using UnityEngine;
using toio;
using Cysharp.Threading.Tasks;

public class ToioPositionController : MonoBehaviour
{
    private CubeManager cubeManager;
    private Cube cube;

    // Inspector viewable status
    [Header("Current Status")]
    public int currentX;
    public int currentY;
    public int currentAngle;

    async void Start()
    {
        Debug.Log("ToioPositionController: Start method called.");
        // 1. Connect to multiple cubes to find the right one
        cubeManager = new CubeManager(ConnectType.Real);
        Debug.Log("ToioPositionController: Scanning for cubes...");
        
        // Connect to up to 4 cubes to ensure we catch the one on the mat
        var cubes = await cubeManager.MultiConnect(4);

        if (cubes != null && cubes.Length > 0)
        {
            Debug.Log($"Connected to {cubes.Length} cubes. Checking which one is on the mat...");
            
            // 2. Configure Sensors for ALL cubes first
            foreach (var c in cubes)
            {
                await c.ConfigIDNotification(500, Cube.IDNotificationType.OnChanged);
                await c.ConfigIDMissedNotification(500);
            }

            // Wait for sensor data to arrive
            await UniTask.Delay(1000);

            // 3. Find the cube that has valid coordinates
            foreach (var c in cubes)
            {
                Debug.Log($"Cube ID:{c.id} Pos:({c.x},{c.y})");
                if (c.x != 0 || c.y != 0)
                {
                    cube = c;
                    break;
                }
            }

            // Fallback: If none found, warn user but pick the first one (or keep searching)
            if (cube == null)
            {
                Debug.LogWarning("No cube detected on the mat! Picking the first one anyway, but it may not work.");
                cube = cubes[0];
            }
            else
            {
                Debug.Log($"Found valid cube! ID: {cube.id}");
                // Disconnect others to save battery/bandwidth? 
                // Optional: keep them connected or disconnect. For now, let's keep it simple and just ignore them.
                // But to be clean, let's disconnect unused ones.
                foreach(var c in cubes)
                {
                    if (c != cube) cubeManager.Disconnect(c);
                }
            }

            // 4. Setup Active Cube
            cube.TurnLedOn(0, 255, 0, 500); // Green flash
            cube.targetMoveCallback.AddListener("PositionController", OnTargetMoveResult);
            cube.idCallback.AddListener("DebugLog", (c) => Debug.Log($"[Callback] ID Updated: ({c.x}, {c.y}) Angle: {c.angle}"));
            cube.idMissedCallback.AddListener("DebugLog", (c) => Debug.LogWarning("[Callback] ID Missed!"));
        }
        else
        {
            Debug.LogWarning("Connection failed. No cubes found.");
        }
    }

    void Update()
    {
        if (cube == null) return;

        // 3. Status Monitor
        currentX = cube.x;
        currentY = cube.y;
        currentAngle = cube.angle;
        
        // Log status every 1 second
        if (Time.time % 2.0f < 0.05f)
        {
             Debug.Log($"[StatusCheck] X:{cube.x} Y:{cube.y} Angle:{cube.angle} IsConnected:{cube.isConnected}");
        }

        // 4. Trigger
        if (Input.GetKeyDown(KeyCode.Space))
        {
            Debug.Log($"移動開始要求: 現在地({cube.x}, {cube.y}) 角度{cube.angle} -> 目標(250, 250)");

            if (cube.x == 0 && cube.y == 0)
            {
                Debug.LogWarning("座標が(0,0)です。マット上の位置を認識できていない可能性があります。マットの上に置いてあるか、センサーが汚れていないか確認してください。");
                // Force request sensor data just in case
                // cube.RequestSensor(); // Deprecated but might help debug? No, rely on Config.
            }

            // Always try to move even if 0,0 to see if it wakes up
            cube.TargetMove(
                targetX: 250, 
                targetY: 250, 
                targetAngle: 0, 
                maxSpd: 80, 
                targetMoveType: Cube.TargetMoveType.RotatingMove
            );
        }
    }

    void OnTargetMoveResult(Cube c, int configID, Cube.TargetMoveRespondType response)
    {
        switch (response)
        {
            case Cube.TargetMoveRespondType.Normal:
                Debug.Log("到着しました");
                break;
            case Cube.TargetMoveRespondType.ToioIDmissed:
                Debug.Log("マットから外れました");
                break;
            default:
                Debug.Log($"移動終了: {response}");
                break;
        }
    }
}
