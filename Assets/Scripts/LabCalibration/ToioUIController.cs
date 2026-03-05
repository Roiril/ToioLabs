using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;

namespace ToioLabs.UI
{
    [RequireComponent(typeof(RectTransform))]
    public class ToioUIController : MonoBehaviour
    {
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

        // Presenter Properties
        public float SendInterval => _sendInterval;
        public float MinDistance => _minDistance;
        public float BatteryUpdateInterval => _batteryUpdateInterval;
        public float PosUpdateInterval => _posUpdateInterval;

        // Events for Presenter
        public event Action OnSpacePressed;
        public event Action OnRecalibrateRequested;
        public event Action<Vector2> OnTouchInput;

        // Private Fields
        private LabCalibration.ToioTouchPanelPresenter _presenter;
        private bool _panelLive;
        private RectTransform _livePanelRect;
        private TouchInputReceiver _inputReceiver;
        private RectTransform _originalPanelRect;
        private Transform _originalIndicatorParent;

        // Static strings
        private const string InstrGuide    = "Guide:\n[Click] Move Toio\n[Space] Calibrate\n[P] Patrol";
        private const string InstrWaitFl   = "Calib:\nMove Front-Left & Space";
        private const string InstrWaitFr   = "Calib:\nMove Front-Right & Space";
        private const string InstrWaitBr   = "Calib:\nMove Back-Right & Space";
        private const string InstrWaitBl   = "Calib:\nMove Back-Left & Space";
        private const string InstrCalibDone = "Done!\nClick panel to move.";

        private void Awake()
        {
            if (_touchPanelRect == null) _touchPanelRect = GetComponent<RectTransform>();
            if (_touchPanelImage == null) _touchPanelImage = _touchPanelRect.GetComponent<Image>();
            if (_toioIndicator == null) _toioIndicator = transform.Find("ToioIndicator")?.GetComponent<RectTransform>();

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

            if (_touchPanelImage != null) _touchPanelImage.enabled = false;
        }

        private void Start()
        {
            _presenter = new LabCalibration.ToioTouchPanelPresenter(this);
            _presenter.Start();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                OnSpacePressed?.Invoke();
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

        private void OnDestroy()
        {
            _presenter?.Dispose();
        }

        public void StartRecalibration()
        {
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);

            OnRecalibrateRequested?.Invoke();
        }

        public void ResetForCalibration()
        {
            if (_inputReceiver != null)
            {
                _inputReceiver.OnInput -= HandleTouchInput;
                _inputReceiver = null;
            }

            if (_toioIndicator != null && _originalIndicatorParent != null)
            {
                _toioIndicator.SetParent(_originalIndicatorParent, false);
                _toioIndicator.anchoredPosition = Vector2.zero;
            }

            _livePanelRect = null;
            _panelLive = false;

            if (_calibVisualizer != null)
                _calibVisualizer.Reset();

            if (_touchPanelImage != null)
                _touchPanelImage.enabled = false;
        }

        public void UpdateConnectionStatus(string status, Color color)
        {
            if (_connectionStatusText != null)
            {
                _connectionStatusText.text = $"Status: {status}";
                _connectionStatusText.color = color;
            }
        }

        public void UpdateInstructions(LabCalibration.ToioCalibration2DService.CalibrationState state)
        {
            if (_instructionText == null) return;
            switch (state)
            {
                case LabCalibration.ToioCalibration2DService.CalibrationState.None:      _instructionText.text = InstrGuide;      break;
                case LabCalibration.ToioCalibration2DService.CalibrationState.WaitingFL: _instructionText.text = InstrWaitFl;     break;
                case LabCalibration.ToioCalibration2DService.CalibrationState.WaitingFR: _instructionText.text = InstrWaitFr;     break;
                case LabCalibration.ToioCalibration2DService.CalibrationState.WaitingBR: _instructionText.text = InstrWaitBr;     break;
                case LabCalibration.ToioCalibration2DService.CalibrationState.WaitingBL: _instructionText.text = InstrWaitBl;     break;
                case LabCalibration.ToioCalibration2DService.CalibrationState.Done:      _instructionText.text = InstrCalibDone;  break;
            }
        }

        public void UpdateCalibrationStatus(LabCalibration.ToioCalibration2DService.CalibrationState state, int count)
        {
            if (_calibrationStatusText == null) return;
            if (state == LabCalibration.ToioCalibration2DService.CalibrationState.Done)
            {
                _calibrationStatusText.text = "Calibration: Done";
                _calibrationStatusText.color = Color.green;
            }
            else
            {
                _calibrationStatusText.text = $"Calibration: [{count}/4]";
                _calibrationStatusText.color = Color.yellow;
            }
        }

        public void ShowCalibrationVisualFeedback(int index, Vector2Int matPos)
        {
            if (_calibVisualizer == null) return;

            Vector2 panelPos = MapMatToLocal(_touchPanelRect, matPos.x, matPos.y);
            _calibVisualizer.ShowDot(index, panelPos);

            if (index == 1) _calibVisualizer.ShowEdge(0, 0, 1);
            else if (index == 2) _calibVisualizer.ShowEdge(1, 1, 2);
            else if (index == 3)
            {
                _calibVisualizer.ShowEdge(2, 2, 3);
                _calibVisualizer.ShowEdge(3, 3, 0);
            }
        }

        public void ApplyCalibrationToView()
        {
            if (_calibVisualizer != null)
            {
                _calibVisualizer.ShowFilledRect();
                _calibVisualizer.MakePanelLive(OnPanelLive);
            }
            else
            {
                _panelLive = true;
                if (_touchPanelImage != null) _touchPanelImage.enabled = true;
            }
        }

        private void OnPanelLive(RectTransform liveRect)
        {
            if (liveRect == null)
            {
                _panelLive = true;
                return;
            }

            _livePanelRect = liveRect;
            _inputReceiver = liveRect.GetComponent<TouchInputReceiver>();
            if (_inputReceiver != null)
            {
                _inputReceiver.OnInput += HandleTouchInput;
            }

            if (_toioIndicator != null)
            {
                _toioIndicator.SetParent(liveRect, false);
                _toioIndicator.anchoredPosition = Vector2.zero;
            }

            _panelLive = true;
        }

        private void HandleTouchInput(Vector2 screenPos)
        {
            OnTouchInput?.Invoke(screenPos);
        }

        public void UpdateIndicatorPosition(int cubeX, int cubeY)
        {
            if (_panelLive && _livePanelRect != null)
            {
                // Must map coordinates relative to the CalibFilledRect if visual feedback is used
                if (_toioIndicator != null && _calibVisualizer != null)
                {
                    // For the filled rect, we pass the bounds from calibration service indirectly, 
                    // actually the MapMatToLocal original method handles this properly but let's just reverse use MapMatToLocal inside Presenter? 
                    // No, MapMatToLocal was mapping based on standard mat ranges anyway.
                    _toioIndicator.anchoredPosition = MapMatToLocal(_livePanelRect, cubeX, cubeY);
                }
            }
            else
            {
                if (_toioIndicator != null && _touchPanelRect != null)
                {
                    _toioIndicator.anchoredPosition = MapMatToLocal(_touchPanelRect, cubeX, cubeY);
                }
            }
        }

        public void UpdatePositionStatus(int cubeX, int cubeY)
        {
            if (_matPosStatusText != null)
            {
                _matPosStatusText.text = $"Pos: ({cubeX}, {cubeY})";
            }
        }

        public void UpdateBatteryStatus(int battery)
        {
            if (_batteryStatusText != null)
            {
                _batteryStatusText.text = $"Bat: {battery}%";
            }
        }

        public bool TryGetMatTargetPosition(Vector2 screenPos, out int targetX, out int targetY)
        {
            targetX = 0;
            targetY = 0;
            if (_livePanelRect == null) return false;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_livePanelRect, screenPos, null, out Vector2 localPoint))
            {
                float w = _livePanelRect.rect.width;
                float h = _livePanelRect.rect.height;

                float normX = Mathf.InverseLerp(-w * 0.5f, w * 0.5f, localPoint.x);
                float normY = Mathf.InverseLerp(h * 0.5f, -h * 0.5f, localPoint.y);

                targetX = (int)Mathf.Lerp(_matMinX, _matMaxX, normX);
                targetY = (int)Mathf.Lerp(_matMinY, _matMaxY, normY);

                targetX = Mathf.Clamp(targetX, Mathf.Min(_matMinX, _matMaxX), Mathf.Max(_matMinX, _matMaxX));
                targetY = Mathf.Clamp(targetY, Mathf.Min(_matMinY, _matMaxY), Mathf.Max(_matMinY, _matMaxY));
                
                return true;
            }
            return false;
        }

        private Vector2 MapMatToLocal(RectTransform rect, int matX, int matY)
        {
            float normX = Mathf.InverseLerp(_matMinX, _matMaxX, matX);
            float normY = Mathf.InverseLerp(_matMinY, _matMaxY, matY);

            float w = rect.rect.width;
            float h = rect.rect.height;

            float uiX = Mathf.Lerp(-w * 0.5f, w * 0.5f, normX);
            float uiY = Mathf.Lerp(h * 0.5f, -h * 0.5f, normY);

            return new Vector2(uiX, uiY);
        }
    }
}
