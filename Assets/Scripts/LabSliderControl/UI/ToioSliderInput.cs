using System;
using UnityEngine;
using UnityEngine.UI;

namespace ToioLabs.UI
{
    /// <summary>
    /// View component for the Slider Control scene.
    /// Handles Unity inspector references and UI events, but delegates logic to ToioSliderPresenter.
    /// </summary>
    public class ToioSliderInput : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Slider _slider;
        [SerializeField] private TMPro.TMP_Text _statusText;
        [SerializeField] private TMPro.TMP_Text _calibrationText;
        [SerializeField] private Button _recalibrateButton;

        [Header("Mat Coordinate Settings")]
        [Tooltip("Left edge X coordinate on the toio mat")]
        [SerializeField] private int _leftBoundX = 455;
        [Tooltip("Right edge X coordinate on the toio mat")]
        [SerializeField] private int _rightBoundX = 45;

        [Header("Smoothing Settings")]
        [Tooltip("Time it takes to smooth the slider movement")]
        [SerializeField] private float _smoothTime = 0.1f;

        // Properties for Presenter
        public int DefaultLeftBoundX => _leftBoundX;
        public int DefaultRightBoundX => _rightBoundX;

        // Events for Presenter
        public event Action OnSpacePressed;
        public event Action OnRecalibrateRequested;
        public event Action<float> OnSliderValueChangedEvent;

        private ToioSliderPresenter _presenter;
        private bool _isUpdatingSliderProgrammatically = false;
        private float _currentVelocity;

        private void Start()
        {
            if (_recalibrateButton != null)
            {
                _recalibrateButton.onClick.AddListener(() => OnRecalibrateRequested?.Invoke());
            }

            if (_slider != null)
            {
                _slider.onValueChanged.AddListener(OnSliderValueChanged);
            }

            _presenter = new ToioSliderPresenter(this);
            _presenter.Start();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                OnSpacePressed?.Invoke();
            }
        }

        private void OnDestroy()
        {
            if (_recalibrateButton != null)
            {
                _recalibrateButton.onClick.RemoveAllListeners();
            }

            if (_slider != null)
            {
                _slider.onValueChanged.RemoveListener(OnSliderValueChanged);
            }

            _presenter?.Dispose();
        }

        private void OnSliderValueChanged(float value)
        {
            if (_isUpdatingSliderProgrammatically) return;
            OnSliderValueChangedEvent?.Invoke(value);
        }

        public void UpdateStatusText(string message)
        {
            if (_statusText != null)
            {
                _statusText.text = message;
            }
        }

        public void UpdateCalibrationText(string message)
        {
            if (_calibrationText != null)
            {
                _calibrationText.text = message;
            }
        }

        public void UpdateSliderValueWithoutTriggeringEvent(float normalizedValue)
        {
            if (_slider == null) return;

            _isUpdatingSliderProgrammatically = true;
            _slider.value = Mathf.SmoothDamp(_slider.value, normalizedValue, ref _currentVelocity, _smoothTime);
            _isUpdatingSliderProgrammatically = false;
        }
    }
}
