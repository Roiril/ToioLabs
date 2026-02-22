using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using toio;
using Cysharp.Threading.Tasks;
using TMPro;

namespace ToioLabs.UI
{
    [RequireComponent(typeof(RectTransform))]
    public class ToioUIController : MonoBehaviour, IDragHandler, IPointerDownHandler
    {
        public enum CalibrationState
        {
            None,
            WaitingFL, // Front-Left
            WaitingFR, // Front-Right
            WaitingBR, // Back-Right
            WaitingBL, // Back-Left
            Done
        }

        [Header("UI References (Assign in Inspector)")]
        [SerializeField, Tooltip("Main panel for touch input. Use this for coordinate mapping.")]
        private RectTransform _touchPanelRect;

        [SerializeField, Tooltip("Indicator that shows the cube's real-time position on the panel.")]
        private RectTransform _toioIndicator;

        [SerializeField, Tooltip("Image component of the touch panel for visibility control.")]
        private Image _touchPanelImage;

        [Header("Status Fields")]
        [SerializeField] private TextMeshProUGUI _connectionStatusText;
        [SerializeField] private TextMeshProUGUI _batteryStatusText;
        [SerializeField] private TextMeshProUGUI _matPosStatusText;
        [SerializeField] private TextMeshProUGUI _instructionText;
        [SerializeField] private TextMeshProUGUI _calibrationStatusText;

        [Header("Mat Coordinate Settings (Standard Toio Mat)")]
        [SerializeField] private float _panelWidth = 400f;
        [SerializeField] private float _panelHeight = 400f;
        [SerializeField] private int _matMinX = 45;
        [SerializeField] private int _matMaxX = 455;
        [SerializeField] private int _matMinY = 45;
        [SerializeField] private int _matMaxY = 455;

        [Header("Telemetry Settings")]
        [SerializeField] private float _sendInterval = 0.1f;
        [SerializeField] private float _minDistance = 20f;
        [SerializeField] private float _batteryUpdateInterval = 10f;
        [SerializeField] private float _posUpdateInterval = 0.1f;

        [Header("Panel Scale")]
        [SerializeField, Tooltip("Mouse-wheel scale sensitivity.")]
        private float _scrollSensitivity = 0.1f;
        [SerializeField, Tooltip("Minimum scale for the live touch panel.")]
        private float _panelScaleMin = 0.3f;
        [SerializeField, Tooltip("Maximum scale for the live touch panel.")]
        private float _panelScaleMax = 3.0f;

        private CubeManager _cubeManager;
        private Cube _connectedCube;
        private float _lastSendTime;
        private Vector2 _lastSentMatPos;
        private float _nextBatteryUpdateTime;
        private float _nextPosUpdateTime;

        private CalibrationState _currentState = CalibrationState.None;
        private System.Collections.Generic.List<Vector2Int> _calibrationPoints = new System.Collections.Generic.List<Vector2Int>();
        private Vector2 _initialPanelSize;
        private bool _panelLive;  // true after MakePanelLive completes

        [Header("Calibration Visual Feedback")]
        [SerializeField, Tooltip("Visual feedback controller for calibration points.")]
        private CalibrationVisualizer _calibVisualizer;

        // Static strings to avoid GC allocation
        private const string INSTR_GUIDE = "Guide:\n[Click] Move Toio\n[Space] Calibrate\n[P] Patrol";
        private const string INSTR_WAIT_FL = "Calibration:\nMove to Front-Left and press Space";
        private const string INSTR_WAIT_FR = "Calibration:\nMove to Front-Right and press Space";
        private const string INSTR_WAIT_BR = "Calibration:\nMove to Back-Right and press Space";
        private const string INSTR_WAIT_BL = "Calibration:\nMove to Back-Left and press Space";
        private const string INSTR_CALIB_DONE = "Calibration: Done!\nAspect ratio adjusted.";

        private void Awake()
        {
            if (_touchPanelRect == null) _touchPanelRect = GetComponent<RectTransform>();
            if (_touchPanelImage == null) _touchPanelImage = _touchPanelRect.GetComponent<Image>();
            if (_toioIndicator == null) _toioIndicator = transform.Find("ToioIndicator")?.GetComponent<RectTransform>();

            var dashboard = transform.parent;
            if (dashboard != null)
            {
                var statusFrame = dashboard.Find("StatusFrame");
                if (statusFrame != null)
                {
                    if (_connectionStatusText == null) _connectionStatusText = statusFrame.Find("ConnectionStatus")?.GetComponent<TextMeshProUGUI>();
                    if (_batteryStatusText == null) _batteryStatusText = statusFrame.Find("BatteryStatus")?.GetComponent<TextMeshProUGUI>();
                    if (_matPosStatusText == null) _matPosStatusText = statusFrame.Find("MatPosStatus")?.GetComponent<TextMeshProUGUI>();
                }
                if (_instructionText == null) _instructionText = dashboard.Find("InstructionFrame")?.GetComponent<TextMeshProUGUI>();
                if (_calibrationStatusText == null) _calibrationStatusText = dashboard.Find("StatusFrame")?.Find("CalibrationStatus")?.GetComponent<TextMeshProUGUI>();
            }

            // Panel is hidden until calibration is done
            if (_touchPanelImage != null) _touchPanelImage.enabled = false;
        }

        private void Start()
        {
            // Capture initial panel size after layout pass
            _initialPanelSize = _touchPanelRect.rect.size;
            if (_initialPanelSize == Vector2.zero)
                _initialPanelSize = _touchPanelRect.sizeDelta;

            ConnectAndCalibrate().Forget();
        }

        private async UniTaskVoid ConnectAndCalibrate()
        {
            UpdateConnectionStatus("Scanning...", Color.yellow);
            UpdateInstructions();

            _cubeManager = new CubeManager(ConnectType.Real);
            var cubes = await _cubeManager.MultiConnect(1);

            if (cubes != null && cubes.Length > 0)
            {
                _connectedCube = cubes[0];
                _connectedCube.TurnLedOn(0, 0, 255, 500);
                UpdateConnectionStatus("Connected", Color.green);

#if UNITY_EDITOR
                Debug.Log($"[ToioUI] Connected: {_connectedCube.id}");
#endif

                await _connectedCube.ConfigIDNotification(500, Cube.IDNotificationType.OnChanged);
                await _connectedCube.ConfigIDMissedNotification(500);

                // Re-capture size after all awaits (layout guaranteed)
                _initialPanelSize = _touchPanelRect.rect.size;
                if (_initialPanelSize == Vector2.zero)
                    _initialPanelSize = _touchPanelRect.sizeDelta;

                EnterCalibration();
            }
            else
            {
                UpdateConnectionStatus("Disconnected", Color.red);
#if UNITY_EDITOR
                Debug.LogWarning("[ToioUI] Connection Failed: No cubes found.");
#endif
            }
        }

        void Update()
        {
            if (_connectedCube == null) return;

            UpdateTelemetry();

            if (Input.GetKeyDown(KeyCode.Space))
            {
                RecordCalibrationPoint();
            }

            // Mouse-wheel scale for the live touch panel
            if (_panelLive && _touchPanelRect != null)
            {
                float scroll = Input.mouseScrollDelta.y;
                if (scroll != 0f)
                {
                    Vector3 s = _touchPanelRect.localScale;
                    float newScale = Mathf.Clamp(s.x + scroll * _scrollSensitivity, _panelScaleMin, _panelScaleMax);
                    _touchPanelRect.localScale = new Vector3(newScale, newScale, 1f);
                }
            }
        }

        private void UpdateTelemetry()
        {
            if (_toioIndicator != null)
            {
                _toioIndicator.anchoredPosition = MapMatToLocal(_connectedCube.x, _connectedCube.y);
            }

            if (Time.time >= _nextPosUpdateTime)
            {
                if (_matPosStatusText != null)
                {
                    _matPosStatusText.text = $"Pos: ({_connectedCube.x}, {_connectedCube.y})";
                }
                _nextPosUpdateTime = Time.time + _posUpdateInterval;
            }

            if (Time.time >= _nextBatteryUpdateTime)
            {
                if (_batteryStatusText != null)
                {
                    _batteryStatusText.text = $"Battery: {_connectedCube.battery}%";
                }
                _nextBatteryUpdateTime = Time.time + _batteryUpdateInterval;
            }

            if (!_connectedCube.isConnected)
            {
                UpdateConnectionStatus("Disconnected", Color.red);
            }
        }

        private Vector2 MapMatToLocal(int matX, int matY)
        {
            float normX = Mathf.InverseLerp(_matMinX, _matMaxX, matX);
            float normY = Mathf.InverseLerp(_matMinY, _matMaxY, matY);

            float w = _touchPanelRect.rect.width;
            float h = _touchPanelRect.rect.height;

            float uiX = Mathf.Lerp(-w / 2f, w / 2f, normX);
            float uiY = Mathf.Lerp(-h / 2f, h / 2f, normY);

            return new Vector2(uiX, uiY);
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
            if (_connectedCube == null || _touchPanelRect == null || _currentState != CalibrationState.Done) return;

            Vector2 localPoint;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_touchPanelRect, screenPos, null, out localPoint))
            {
                float w = _touchPanelRect.rect.width;
                float h = _touchPanelRect.rect.height;

                float normX = Mathf.InverseLerp(-w / 2f, w / 2f, localPoint.x);
                float normY = Mathf.InverseLerp(-h / 2f, h / 2f, localPoint.y);

                int targetX = (int)Mathf.Lerp(_matMinX, _matMaxX, normX);
                int targetY = (int)Mathf.Lerp(_matMinY, _matMaxY, normY);

                targetX = Mathf.Clamp(targetX, Mathf.Min(_matMinX, _matMaxX), Mathf.Max(_matMinX, _matMaxX));
                targetY = Mathf.Clamp(targetY, Mathf.Min(_matMinY, _matMaxY), Mathf.Max(_matMinY, _matMaxY));

                if (Time.time - _lastSendTime >= _sendInterval && Vector2.Distance(new Vector2(targetX, targetY), _lastSentMatPos) >= _minDistance)
                {
                    MoveCube(targetX, targetY);
                }
            }
        }

        private void MoveCube(int x, int y)
        {
            _connectedCube.TargetMove(targetX: x, targetY: y, targetAngle: 0, maxSpd: 80, targetMoveType: Cube.TargetMoveType.RotatingMove);
            _lastSendTime = Time.time;
            _lastSentMatPos = new Vector2(x, y);
        }

        private void UpdateConnectionStatus(string status, Color color)
        {
            if (_connectionStatusText != null)
            {
                _connectionStatusText.text = $"Status: {status}";
                _connectionStatusText.color = color;
            }
        }

        private void UpdateInstructions()
        {
            if (_instructionText == null) return;
            switch (_currentState)
            {
                case CalibrationState.None:      _instructionText.text = INSTR_GUIDE;     break;
                case CalibrationState.WaitingFL: _instructionText.text = INSTR_WAIT_FL;  break;
                case CalibrationState.WaitingFR: _instructionText.text = INSTR_WAIT_FR;  break;
                case CalibrationState.WaitingBR: _instructionText.text = INSTR_WAIT_BR;  break;
                case CalibrationState.WaitingBL: _instructionText.text = INSTR_WAIT_BL;  break;
                case CalibrationState.Done:      _instructionText.text = INSTR_CALIB_DONE; break;
            }
        }

        // ─────── Calibration ───────

        private void EnterCalibration()
        {
            _calibrationPoints.Clear();
            _currentState = CalibrationState.WaitingFL;
            if (_calibVisualizer != null) _calibVisualizer.Reset();
            UpdateInstructions();
        }

        private void RecordCalibrationPoint()
        {
            if (_connectedCube == null || (_connectedCube.x == 0 && _connectedCube.y == 0)) return;
            if (_currentState == CalibrationState.Done || _currentState == CalibrationState.None) return;

            int idx = _calibrationPoints.Count;
            _calibrationPoints.Add(new Vector2Int(_connectedCube.x, _connectedCube.y));
            _connectedCube.TurnLedOn(255, 255, 0, 500);

            // Visual feedback via CalibrationVisualizer
            if (_calibVisualizer != null)
            {
                Vector2 panelPos = MapMatToLocal(_connectedCube.x, _connectedCube.y);
                _calibVisualizer.ShowDot(idx, panelPos);

                // Draw edges as points accumulate
                // Edge index mapping: 0=FL-FR, 1=FR-BR, 2=BR-BL, 3=BL-FL
                if (idx == 1) _calibVisualizer.ShowEdge(0, 0, 1);       // FL → FR
                else if (idx == 2) _calibVisualizer.ShowEdge(1, 1, 2);  // FR → BR
                else if (idx == 3)
                {
                    _calibVisualizer.ShowEdge(2, 2, 3); // BR → BL
                    _calibVisualizer.ShowEdge(3, 3, 0); // BL → FL (close the loop)
                }
            }

            // Advance state
            switch (_currentState)
            {
                case CalibrationState.WaitingFL: _currentState = CalibrationState.WaitingFR; break;
                case CalibrationState.WaitingFR: _currentState = CalibrationState.WaitingBR; break;
                case CalibrationState.WaitingBR: _currentState = CalibrationState.WaitingBL; break;
                case CalibrationState.WaitingBL: _currentState = CalibrationState.Done;      break;
            }

            if (_currentState == CalibrationState.Done)
            {
                ApplyCalibration();
            }

            UpdateInstructions();
            UpdateCalibrationStatus();
        }



        private void UpdateCalibrationStatus()
        {
            if (_calibrationStatusText == null) return;
            if (_currentState == CalibrationState.Done)
            {
                _calibrationStatusText.text = "Calibration: Done";
                _calibrationStatusText.color = Color.green;
            }
            else
            {
                _calibrationStatusText.text = $"Calibration: [{_calibrationPoints.Count}/4]";
                _calibrationStatusText.color = Color.yellow;
            }
        }

        private void ApplyCalibration()
        {
            if (_calibrationPoints.Count < 4) return;

            // FL:0, FR:1, BR:2, BL:3
            int minX = Mathf.Min(_calibrationPoints[0].x, _calibrationPoints[3].x);
            int maxX = Mathf.Max(_calibrationPoints[1].x, _calibrationPoints[2].x);
            int minY = Mathf.Min(_calibrationPoints[0].y, _calibrationPoints[1].y);
            int maxY = Mathf.Max(_calibrationPoints[2].y, _calibrationPoints[3].y);

            _matMinX = minX; _matMaxX = maxX; _matMinY = minY; _matMaxY = maxY;

            // Note: panel is no longer pre-resized here.
            // The CalibFilledRect (created in ShowFilledRect) defines the live panel shape.

            _connectedCube.TurnLedOn(0, 255, 0, 1000);

            // Fade out dots/edges, keep filled rect — swap _touchPanelRect to it
            if (_calibVisualizer != null)
            {
                _calibVisualizer.ShowFilledRect();
                _calibVisualizer.MakePanelLive(liveRect =>
                {
                    if (liveRect != null)
                    {
                        _touchPanelRect = liveRect;
                        var img = liveRect.GetComponent<Image>();
                        if (img != null) img.raycastTarget = true;
                    }
                    _panelLive = true;
                });
            }
            else
            {
                if (_touchPanelImage != null) _touchPanelImage.enabled = true;
            }

#if UNITY_EDITOR
            Debug.Log($"[ToioUI] Calibration done. Mat: X[{_matMinX},{_matMaxX}] Y[{_matMinY},{_matMaxY}]. Panel: {_touchPanelRect.sizeDelta}");
#endif
        }
    }
}
