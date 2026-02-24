using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using toio;

namespace ToioLabs.UI
{
    public class ToioSliderInput : MonoBehaviour
    {
        private enum CalibrationState
        {
            WaitingForLeft,
            WaitingForRight,
            Completed
        }

        [Header("UI References")]
        [SerializeField] private Slider _slider;
        [SerializeField] private TMPro.TMP_Text _statusText;
        [SerializeField] private TMPro.TMP_Text _calibrationText;
        [SerializeField] private Button _recalibrateButton;

        [Header("Mat Coordinate Settings")]
        [Tooltip("Minimum X coordinate on the toio mat")]
        [SerializeField] private int _minMatX = 45;
        [Tooltip("Maximum X coordinate on the toio mat")]
        [SerializeField] private int _maxMatX = 455;

        [Header("Smoothing Settings")]
        [Tooltip("Time it takes to smooth the slider movement")]
        [SerializeField] private float _smoothTime = 0.1f;

        private CubeManager _cubeManager;
        private bool _isConnected = false;
        private CalibrationState _currentState = CalibrationState.WaitingForLeft;
        private float _currentVelocity;

        private void Start()
        {
            if (_recalibrateButton != null)
            {
                _recalibrateButton.onClick.AddListener(StartCalibration);
            }

            _cubeManager = new CubeManager(ConnectType.Real);
            StartCalibration();
            ConnectToCubeAsync().Forget();
        }

        private void OnDestroy()
        {
            if (_recalibrateButton != null)
            {
                _recalibrateButton.onClick.RemoveListener(StartCalibration);
            }
        }

        public void StartCalibration()
        {
            _currentState = CalibrationState.WaitingForLeft;
            UpdateCalibrationText("1. Place toio at the LEFT edge, then press Space!");
        }

        private async UniTaskVoid ConnectToCubeAsync()
        {
            UpdateStatusText("Connecting to toio cube...");
            
            // Connect to a single cube
            var cube = await _cubeManager.SingleConnect();
            
            if (cube != null)
            {
                _isConnected = true;
                UpdateStatusText("Connected: " + _cubeManager.cubes[0].id);
            }
            else
            {
                UpdateStatusText("Connection failed. Check Bluetooth and Cube.");
            }
        }

        private void Update()
        {
            if (!_isConnected || _cubeManager.cubes.Count == 0) return;

            var cube = _cubeManager.cubes[0];

            switch (_currentState)
            {
                case CalibrationState.WaitingForLeft:
                    if (Input.GetKeyDown(KeyCode.Space))
                    {
                        _minMatX = cube.x;
                        _currentState = CalibrationState.WaitingForRight;
                        UpdateCalibrationText("2. Now place it at the RIGHT edge, then press Space!");
                        Debug.Log($"Left Coordinate Recorded: {_minMatX}");
                    }
                    break;

                case CalibrationState.WaitingForRight:
                    if (Input.GetKeyDown(KeyCode.Space))
                    {
                        _maxMatX = cube.x;
                        _currentState = CalibrationState.Completed;
                        UpdateCalibrationText("✨ All Set! Try moving the toio! ✨");
                        Debug.Log($"Right Coordinate Recorded: {_maxMatX}");
                    }
                    break;

                case CalibrationState.Completed:
                    // Normalize the X coordinate to a 0.0 - 1.0 range based on mat bounds
                    float normalizedValue = Mathf.InverseLerp(_minMatX, _maxMatX, cube.x);
                    
                    // Smoothly apply the normalized value to the slider
                    if (_slider != null)
                    {
                        _slider.value = Mathf.SmoothDamp(_slider.value, normalizedValue, ref _currentVelocity, _smoothTime);
                    }
                    break;
            }
        }

        private void UpdateStatusText(string message)
        {
            if (_statusText != null)
            {
                _statusText.text = message;
            }
        }

        private void UpdateCalibrationText(string message)
        {
            if (_calibrationText != null)
            {
                _calibrationText.text = message;
            }
        }
    }
}
