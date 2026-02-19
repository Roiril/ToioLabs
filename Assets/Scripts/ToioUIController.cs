using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using toio;
using Cysharp.Threading.Tasks;

public class ToioUIController : MonoBehaviour, IDragHandler, IPointerDownHandler
{
    [Header("UI References")]
    public RectTransform touchPanelRect;

    [Header("Mat Coordinate Settings")]
    public float panelWidth = 400f;
    public float panelHeight = 400f;
    public int matMinX = 45;
    public int matMaxX = 455;
    public int matMinY = 45;
    public int matMaxY = 455;

    [Header("Control Settings")]
    public float sendInterval = 0.1f; // 0.1秒
    public float minDistance = 20f;   // 20dot

    [Header("Debug")]
    public bool showDebugLogs = true; // デバッグログ表示切り替え

    private CubeManager cubeManager;
    private Cube connectedCube;
    private float lastSendTime;
    private Vector2 lastSentMatPos;

    async void Start()
    {
        // 自動的に参照を取得（アタッチ漏れ防止）
        if (touchPanelRect == null)
        {
            touchPanelRect = GetComponent<RectTransform>();
        }

        // Cube接続処理
        cubeManager = new CubeManager(ConnectType.Real);
        var cubes = await cubeManager.MultiConnect(1);
        
        if (cubes != null && cubes.Length > 0)
        {
            connectedCube = cubes[0];
            connectedCube.TurnLedOn(0, 0, 255, 500); // 接続成功時に青点灯
            Debug.Log($"[ToioUI] Connected: {connectedCube.id}");

            // センサー有効化（座標取得のため）
            await connectedCube.ConfigIDNotification(500, Cube.IDNotificationType.OnChanged);
            await connectedCube.ConfigIDMissedNotification(500);
        }
        else
        {
            Debug.LogWarning("[ToioUI] Connection Failed: No cubes found.");
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        ProcessInput(eventData.position, "PointerDown");
    }

    public void OnDrag(PointerEventData eventData)
    {
        ProcessInput(eventData.position, "Drag");
    }

    private void ProcessInput(Vector2 screenPos, string inputType)
    {
        if (connectedCube == null || touchPanelRect == null) return;

        Vector2 localPoint;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(touchPanelRect, screenPos, null, out localPoint))
        {
            // パネル座標系: 中心が(0,0)の場合と、Pivotによる違いを吸収するため正規化
            // 想定: Anchor/Pivotが中心(0.5, 0.5)でサイズ400x400の場合、localPointは -200~200
            
            // localPointを 0~1 の正規化座標に変換 (Pivot 0.5, 0.5 前提)
            float normalizedX = Mathf.InverseLerp(-panelWidth / 2, panelWidth / 2, localPoint.x);
            float normalizedY = Mathf.InverseLerp(-panelHeight / 2, panelHeight / 2, localPoint.y);

            // 0~1 を Mat座標に変換
            int targetX = (int)Mathf.Lerp(matMinX, matMaxX, normalizedX);
            int targetY = (int)Mathf.Lerp(matMinY, matMaxY, normalizedY);
            
            // Clamp to mat bounds (Correctly handle min > max cases for inverted axes)
            int clampMinX = Mathf.Min(matMinX, matMaxX);
            int clampMaxX = Mathf.Max(matMinX, matMaxX);
            int clampMinY = Mathf.Min(matMinY, matMaxY);
            int clampMaxY = Mathf.Max(matMinY, matMaxY);

            targetX = Mathf.Clamp(targetX, clampMinX, clampMaxX);
            targetY = Mathf.Clamp(targetY, clampMinY, clampMaxY);

            if (showDebugLogs)
            {
                Debug.Log($"[ToioUI] Input({inputType}) Screen:{screenPos} -> Local:{localPoint} -> Norm:({normalizedX:F2}, {normalizedY:F2}) -> Mat:({targetX}, {targetY})");
            }

            // 送信判定 (Throttling)
            if (ShouldSendCommand(new Vector2(targetX, targetY)))
            {
                MoveCube(targetX, targetY);
            }
        }
    }

    private bool ShouldSendCommand(Vector2 targetPos)
    {
        float timeSinceLast = Time.time - lastSendTime;
        float distance = Vector2.Distance(targetPos, lastSentMatPos);

        bool shouldSend = (timeSinceLast >= sendInterval && distance >= minDistance);

        if (!shouldSend && showDebugLogs)
        {
            // Debug.Log($"[ToioUI] Skipped: Time={timeSinceLast:F2}/{sendInterval}, Dist={distance:F1}/{minDistance}");
        }

        return shouldSend;
    }

    // Calibration
    private System.Collections.Generic.List<Vector2Int> calibrationPoints = new System.Collections.Generic.List<Vector2Int>();
    private System.Collections.Generic.List<GameObject> markers = new System.Collections.Generic.List<GameObject>();

    void Update()
    {
        if (connectedCube == null) return;

        // Calibration Input
        if (Input.GetKeyDown(KeyCode.Space))
        {
            RecordCalibrationPoint();
        }
    }

    private void RecordCalibrationPoint()
    {
        if (connectedCube == null) return;

        int x = connectedCube.x;
        int y = connectedCube.y;

        Debug.Log($"[ToioUI] Calibration Attempt: Raw Coords ({x}, {y})");

        // Mat coordinate validity check
        if (x == 0 && y == 0)
        {
            Debug.LogWarning("[ToioUI] Cannot calibrate: Cube not on mat (0,0). Check connection or sensor.");
            return;
        }

        calibrationPoints.Add(new Vector2Int(x, y));
        Debug.Log($"[ToioUI] Calibration Point {calibrationPoints.Count} Recorded: ({x}, {y})");

        // Visual Feedback: Show Marker
        ShowMarker(x, y);

        connectedCube.TurnLedOn(255, 255, 0, 500); // Flash Yellow

        if (calibrationPoints.Count >= 4)
        {
            ApplyCalibration();
            calibrationPoints.Clear();
        }
    }

    private void ShowMarker(int matX, int matY)
    {
        // Create a new UI Object for the marker
        GameObject markerObj = new GameObject("Marker");
        markerObj.transform.SetParent(this.transform); // Child of TouchPanel
        
        // Add Image
        Image img = markerObj.AddComponent<Image>();
        img.color = Color.black; 
        
        // Use a small size
        RectTransform rt = markerObj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(10, 10); // 10x10 dot
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        
        // Convert Mat(x,y) -> UI Local Position
        // We use the CURRENT settings to approximate visual location.
        float normalizedX = Mathf.InverseLerp(matMinX, matMaxX, matX);
        float normalizedY = Mathf.InverseLerp(matMinY, matMaxY, matY); 
        
        // Normalize to UI size (-W/2 to W/2)
        float uiX = Mathf.Lerp(-panelWidth / 2, panelWidth / 2, normalizedX);
        float uiY = Mathf.Lerp(-panelHeight / 2, panelHeight / 2, normalizedY);
        
        rt.anchoredPosition = new Vector2(uiX, uiY);
        
        markers.Add(markerObj);
    }

    private void ApplyCalibration()
    {
        int minX = int.MaxValue;
        int maxX = int.MinValue;
        int minY = int.MaxValue;
        int maxY = int.MinValue;

        foreach (var p in calibrationPoints)
        {
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
        }

        Debug.Log($"[ToioUI] Bounds Detected: X[{minX}~{maxX}], Y[{minY}~{maxY}]");

        // Apply new bounds
        // Panel Left -> Min X
        // Panel Right -> Max X
        // Panel Bottom -> Max Y (Larger value)
        // Panel Top -> Min Y (Smaller value)
        
        matMinX = minX;
        matMaxX = maxX;
        matMinY = maxY; // Bottom of Screen
        matMaxY = minY; // Top of Screen

        Debug.Log($"[ToioUI] Calibration Complete! New Mapping: X[{matMinX}-{matMaxX}], Y[{matMinY}(Bottom)-{matMaxY}(Top)]");
        connectedCube.TurnLedOn(0, 255, 0, 1000); // Green Flash
        
        // Cleanup old markers
        foreach(var m in markers) Destroy(m, 2.0f); 
        markers.Clear();
    }

    private void MoveCube(int x, int y)
    {
        if (connectedCube == null) return;

        // 目標地点へ移動 (回転しながら移動)
        connectedCube.TargetMove(
             targetX: x, 
             targetY: y, 
             targetAngle: 0, 
             maxSpd: 80, 
             targetMoveType: Cube.TargetMoveType.RotatingMove
             );
        
        lastSendTime = Time.time;
        lastSentMatPos = new Vector2(x, y);
        
        if (showDebugLogs)
        {
            Debug.Log($"[ToioUI] Command Sent: MoveTo({x}, {y})");
        }
    }
}
