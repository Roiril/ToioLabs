using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using toio;
using Cysharp.Threading.Tasks;
using TMPro;

namespace ToioLabs.UI
{
    [RequireComponent(typeof(RectTransform))]
    public class ToioUIController : MonoBehaviour
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
        [SerializeField, Tooltip("Main panel RectTransform (original reference, used as fallback).")]
        private RectTransform _touchPanelRect;

        [SerializeField, Tooltip("Indicator that shows the cube's real-time position.")]
        private RectTransform _toioIndicator;

        [SerializeField, Tooltip("Image component of the touch panel (disabled during calibration).")]
        private Image _touchPanelImage;

        [Header("Status Fields")]
        [SerializeField] private TextMeshProUGUI _connectionStatusText;
        [SerializeField] private TextMeshProUGUI _batteryStatusText;
        [SerializeField] private TextMeshProUGUI _matPosStatusText;
        [SerializeField] private TextMeshProUGUI _instructionText;
        [SerializeField] private TextMeshProUGUI _calibrationStatusText;

        [Header("Mat Coordinate Settings (Standard Toio Mat)")]
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

        [Header("Calibration Visual Feedback")]
        [SerializeField, Tooltip("Visual feedback controller for calibration points.")]
        private CalibrationVisualizer _calibVisualizer;

        private CubeManager _cubeManager;
        private Cube _connectedCube;
        private float _lastSendTime;
        private Vector2 _lastSentMatPos;
        private float _nextBatteryUpdateTime;
        private float _nextPosUpdateTime;

        private CalibrationState _currentState = CalibrationState.None;
        private readonly System.Collections.Generic.List<Vector2Int> _calibrationPoints =
            new System.Collections.Generic.List<Vector2Int>();

        // Live panel state
        private bool _panelLive;
        private RectTransform _livePanelRect;        // Points to CalibFilledRect when live
        private TouchInputReceiver _inputReceiver;   // Cached ref to the receiver on FilledRect
        private RectTransform _originalPanelRect;    // Saved reference to restore on recalibration
        private Transform _originalIndicatorParent;  // Saved parent of ToioIndicator

        // Static strings to avoid GC allocation
        private const string InstrGuide    = "Guide:\n[Click] Move Toio\n[Space] Calibrate\n[P] Patrol";
        private const string InstrWaitFl   = "Calibration:\nMove to Front-Left and press Space";
        private const string InstrWaitFr   = "Calibration:\nMove to Front-Right and press Space";
        private const string InstrWaitBr   = "Calibration:\nMove to Back-Right and press Space";
        private const string InstrWaitBl   = "Calibration:\nMove to Back-Left and press Space";
        private const string InstrCalibDone = "Calibration: Done!\nClick the panel to move Toio.";

        // Minimum mat coordinate to consider valid (avoids (0,0) false positive)
        private const int MinValidCoord = 10;

        private void Awake()
        {
            if (_touchPanelRect == null) _touchPanelRect = GetComponent<RectTransform>();
            if (_touchPanelImage == null) _touchPanelImage = _touchPanelRect.GetComponent<Image>();
            if (_toioIndicator == null) _toioIndicator = transform.Find("ToioIndicator")?.GetComponent<RectTransform>();

            // Save originals for recalibration
            _originalPanelRect = _touchPanelRect;
            if (_toioIndicator != null)
                _originalIndicatorParent = _toioIndicator.parent;

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
            if (_panelLive && _livePanelRect != null)
            {
                float scroll = Input.mouseScrollDelta.y;
                if (scroll != 0f)
                {
                    Vector3 s = _livePanelRect.localScale;
                    float newScale = Mathf.Clamp(s.x + scroll * _scrollSensitivity, _panelScaleMin, _panelScaleMax);
                    _livePanelRect.localScale = new Vector3(newScale, newScale, 1f);
                }
            }
        }

        private void UpdateTelemetry()
        {
            // Update indicator position using the active panel rect
            RectTransform activeRect = _panelLive && _livePanelRect != null ? _livePanelRect : _touchPanelRect;
            if (_toioIndicator != null && activeRect != null)
            {
                _toioIndicator.anchoredPosition = MapMatToLocal(activeRect, _connectedCube.x, _connectedCube.y);
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

        /// <summary>
        /// Maps mat coordinates to local-space position relative to the given RectTransform.
        /// </summary>
        private Vector2 MapMatToLocal(RectTransform rect, int matX, int matY)
        {
            float normX = Mathf.InverseLerp(_matMinX, _matMaxX, matX);
            float normY = Mathf.InverseLerp(_matMinY, _matMaxY, matY);

            float w = rect.rect.width;
            float h = rect.rect.height;

            float uiX = Mathf.Lerp(-w * 0.5f, w * 0.5f, normX);
            float uiY = Mathf.Lerp(-h * 0.5f, h * 0.5f, normY);

            return new Vector2(uiX, uiY);
        }

        // ─────── Input from TouchInputReceiver ───────

        private void HandleTouchInput(Vector2 screenPos)
        {
            if (_connectedCube == null || _currentState != CalibrationState.Done) return;
            if (_livePanelRect == null) return;

            Vector2 localPoint;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_livePanelRect, screenPos, null, out localPoint))
            {
                float w = _livePanelRect.rect.width;
                float h = _livePanelRect.rect.height;

                float normX = Mathf.InverseLerp(-w * 0.5f, w * 0.5f, localPoint.x);
                float normY = Mathf.InverseLerp(-h * 0.5f, h * 0.5f, localPoint.y);

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
                case CalibrationState.None:      _instructionText.text = InstrGuide;      break;
                case CalibrationState.WaitingFL: _instructionText.text = InstrWaitFl;     break;
                case CalibrationState.WaitingFR: _instructionText.text = InstrWaitFr;     break;
                case CalibrationState.WaitingBR: _instructionText.text = InstrWaitBr;     break;
                case CalibrationState.WaitingBL: _instructionText.text = InstrWaitBl;     break;
                case CalibrationState.Done:      _instructionText.text = InstrCalibDone;  break;
            }
        }

        // ─────── Calibration ───────

        private void EnterCalibration()
        {
            _calibrationPoints.Clear();
            _currentState = CalibrationState.WaitingFL;
            if (_calibVisualizer != null) _calibVisualizer.Reset();
            UpdateInstructions();
            UpdateCalibrationStatus();
        }

        private void RecordCalibrationPoint()
        {
            if (_connectedCube == null) return;
            if (_currentState == CalibrationState.Done || _currentState == CalibrationState.None) return;

            // Validate position is within a plausible range (not (0,0) or very low)
            if (_connectedCube.x < MinValidCoord && _connectedCube.y < MinValidCoord) return;

            int idx = _calibrationPoints.Count;
            _calibrationPoints.Add(new Vector2Int(_connectedCube.x, _connectedCube.y));
            _connectedCube.TurnLedOn(255, 255, 0, 500);

            // Visual feedback: dot + edges
            if (_calibVisualizer != null)
            {
                Vector2 panelPos = MapMatToLocal(_touchPanelRect, _connectedCube.x, _connectedCube.y);
                _calibVisualizer.ShowDot(idx, panelPos);

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

            _connectedCube.TurnLedOn(0, 255, 0, 1000);

            // Fade out dots/edges, keep filled rect, then swap to live panel
            if (_calibVisualizer != null)
            {
                _calibVisualizer.ShowFilledRect();
                _calibVisualizer.MakePanelLive(OnPanelLive);
            }
            else
            {
                // Fallback: use original panel
                _panelLive = true;
                if (_touchPanelImage != null) _touchPanelImage.enabled = true;
            }

#if UNITY_EDITOR
            Debug.Log($"[ToioUI] Calibration done. Mat: X[{_matMinX},{_matMaxX}] Y[{_matMinY},{_matMaxY}]");
#endif
        }

        private void OnPanelLive(RectTransform liveRect)
        {
            if (liveRect == null)
            {
                _panelLive = true;
                return;
            }

            _livePanelRect = liveRect;

            // Subscribe to touch events from the TouchInputReceiver
            _inputReceiver = liveRect.GetComponent<TouchInputReceiver>();
            if (_inputReceiver != null)
            {
                _inputReceiver.OnInput += HandleTouchInput;
            }

            // Reparent ToioIndicator into FilledRect so coordinates match
            if (_toioIndicator != null)
            {
                _toioIndicator.SetParent(liveRect, false);
                _toioIndicator.anchoredPosition = Vector2.zero;
            }

            _panelLive = true;
        }

        /// <summary>
        /// Public method for recalibration. Called by RecalibrateButton.
        /// Resets all live panel state and re-enters calibration mode.
        /// </summary>
        public void StartRecalibration()
        {
            if (_connectedCube == null) return;

            // Unsubscribe from the old receiver
            if (_inputReceiver != null)
            {
                _inputReceiver.OnInput -= HandleTouchInput;
                _inputReceiver = null;
            }

            // Return ToioIndicator to its original parent
            if (_toioIndicator != null && _originalIndicatorParent != null)
            {
                _toioIndicator.SetParent(_originalIndicatorParent, false);
                _toioIndicator.anchoredPosition = Vector2.zero;
            }

            // Restore original panel reference
            _livePanelRect = null;
            _panelLive = false;

            // Reset visualizer (destroys CalibFilledRect safely)
            if (_calibVisualizer != null)
                _calibVisualizer.Reset();

            // Hide fallback panel image
            if (_touchPanelImage != null)
                _touchPanelImage.enabled = false;

            EnterCalibration();
        }
    }
}
