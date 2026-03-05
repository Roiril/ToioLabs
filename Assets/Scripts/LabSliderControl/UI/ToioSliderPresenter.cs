using System;
using UnityEngine;
using toio;
using ToioLabs.Core;
using System.Threading;

namespace ToioLabs.UI
{
    /// <summary>
    /// Presenter that coordinates the View (ToioSliderInput) and Services (Connection, 1D Calibration, Handle Movement).
    /// Pure C# class.
    /// </summary>
    public class ToioSliderPresenter : IDisposable
    {
        private readonly ToioSliderInput _view;
        private readonly ToioConnectionManager _connectionManager;
        private readonly ToioCalibration1DService _calibrationService;

        private CubeHandle _cubeHandle;
        private CancellationTokenSource _cts;

        private bool _isMovingToio = false;
        private float _targetSliderValue = 0f;
        private float _lastMoveTime = 0f;

        public ToioSliderPresenter(ToioSliderInput view)
        {
            _view = view;
            
            _connectionManager = ServiceLocator.Resolve<ToioConnectionManager>();
            if (_connectionManager == null)
            {
                _connectionManager = new ToioConnectionManager(ConnectType.Real);
                ServiceLocator.Register(_connectionManager);
            }

            _calibrationService = ServiceLocator.Resolve<ToioCalibration1DService>();
            if (_calibrationService == null)
            {
                _calibrationService = new ToioCalibration1DService();
                ServiceLocator.Register(_calibrationService);
            }

            _calibrationService.OnStateChanged += HandleCalibrationStateChanged;
            _calibrationService.OnError += HandleCalibrationError;

            _view.OnSpacePressed += HandleSpacePressed;
            _view.OnRecalibrateRequested += HandleRecalibrateRequested;
            _view.OnSliderValueChangedEvent += HandleSliderValueChanged;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _calibrationService.StartCalibration(_view.DefaultLeftBoundX, _view.DefaultRightBoundX);
            ConnectToCubeAsync().Forget();
        }

        // Update is removed in favor of Event-Driven pattern
        // public void Update(float time) { ... }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();

            if (_calibrationService != null)
            {
                _calibrationService.OnStateChanged -= HandleCalibrationStateChanged;
                _calibrationService.OnError -= HandleCalibrationError;
            }

            if (_view != null)
            {
                _view.OnSpacePressed -= HandleSpacePressed;
                _view.OnRecalibrateRequested -= HandleRecalibrateRequested;
                _view.OnSliderValueChangedEvent -= HandleSliderValueChanged;
            }
        }

        private async Cysharp.Threading.Tasks.UniTaskVoid ConnectToCubeAsync()
        {
            _view.UpdateStatusText("Connecting...");

            var cube = await _connectionManager.ConnectAsync((status, color) => _view.UpdateStatusText(status), _cts.Token);
            if (cube != null && !_cts.IsCancellationRequested)
            {
                _cubeHandle = new CubeHandle(cube);
                cube.idCallback.AddListener("ToioSliderPresenter", HandleCubeIdUpdate);
                _view.UpdateStatusText("Connected: " + cube.id);
                
                MovementLoopAsync(_cts.Token).Forget();
            }
        }

        private async Cysharp.Threading.Tasks.UniTaskVoid MovementLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (!_connectionManager.IsConnected)
                {
                    if (_connectionManager.CubeManager != null && _connectionManager.CubeManager.cubes.Count > 0 && 
                        !_connectionManager.CubeManager.cubes[0].isConnected)
                    {
                        _cubeHandle = null;
                        _connectionManager.Disconnect();
                        ConnectToCubeAsync().Forget();
                        break;
                    }
                }
                else
                {
                    if (_cubeHandle != null) _cubeHandle.Update();

                    if (_calibrationService.CurrentState == ToioCalibration1DService.CalibrationState.Completed && _isMovingToio)
                    {
                        MoveCubeToSliderValue();
                    }
                }

                await Cysharp.Threading.Tasks.UniTask.Delay(TimeSpan.FromMilliseconds(50), cancellationToken: token);
            }
        }

        private void HandleCubeIdUpdate(Cube cube)
        {
            if (_calibrationService.CurrentState == ToioCalibration1DService.CalibrationState.Completed && !_isMovingToio)
            {
                UpdateSliderFromCube(cube.x);
            }
        }

        private void HandleSpacePressed()
        {
            if (_connectionManager.IsConnected)
            {
                _calibrationService.RecordPoint(_connectionManager.ConnectedCube);
            }
        }

        private void HandleRecalibrateRequested()
        {
            _isMovingToio = false;
            _calibrationService.StartCalibration(_view.DefaultLeftBoundX, _view.DefaultRightBoundX);
        }

        private void HandleSliderValueChanged(float value)
        {
            if (_calibrationService.CurrentState != ToioCalibration1DService.CalibrationState.Completed || !_connectionManager.IsConnected)
                return;

            _isMovingToio = true;
            _targetSliderValue = value;
        }

        private void HandleCalibrationStateChanged(ToioCalibration1DService.CalibrationState state)
        {
            switch (state)
            {
                case ToioCalibration1DService.CalibrationState.WaitingForRight:
                    _view.UpdateCalibrationText("1. Place RIGHT & press SPACE");
                    break;
                case ToioCalibration1DService.CalibrationState.WaitingForLeft:
                    _view.UpdateCalibrationText("2. Place LEFT & press SPACE");
                    if (_connectionManager.IsConnected)
                        _view.UpdateStatusText("Connected: " + _connectionManager.ConnectedCube.id);
                    break;
                case ToioCalibration1DService.CalibrationState.Completed:
                    _view.UpdateCalibrationText("Done! Track or Move.");
                    if (_connectionManager.IsConnected)
                        _view.UpdateStatusText("Connected: " + _connectionManager.ConnectedCube.id);
                    break;
            }
        }

        private void HandleCalibrationError(string message)
        {
            _view.UpdateStatusText(message);
        }

        private void UpdateSliderFromCube(int cubeX)
        {
            float t = ((float)cubeX - _calibrationService.LeftBoundX) / (_calibrationService.RightBoundX - _calibrationService.LeftBoundX);
            float normalizedValue = Mathf.Clamp01(t);
            
            _view.UpdateSliderValueWithoutTriggeringEvent(normalizedValue);
        }

        private void MoveCubeToSliderValue()
        {
            if (_cubeHandle == null || _cubeHandle.cube == null) return;

            int targetX = Mathf.RoundToInt(Mathf.Lerp(_calibrationService.LeftBoundX, _calibrationService.RightBoundX, _targetSliderValue));

            toio.MathUtils.Vector targetPos = new toio.MathUtils.Vector(targetX, _calibrationService.CalibratedY);
            toio.MathUtils.Vector diff = targetPos - _cubeHandle.pos;
            double dist = diff.mag;

            if (dist < 15)
            {
                _isMovingToio = false;
                _cubeHandle.Move(0, 0, 100, border: false, order: Cube.ORDER_TYPE.Weak);
                return;
            }

            double targetRad = diff.rad;
            double drad = toio.MathUtils.Utils.Rad(targetRad - _cubeHandle.radPred);
            
            bool moveBackward = Mathf.Abs((float)drad) > Mathf.PI / 2f;
            
            double translate;
            double maxSpd = 60.0;
            
            if (moveBackward)
            {
                drad = toio.MathUtils.Utils.Rad(drad - Mathf.Sign((float)drad) * Mathf.PI);
                translate = -Mathf.Clamp((float)(dist * 1.5), 15f, (float)maxSpd);
            }
            else
            {
                translate = Mathf.Clamp((float)(dist * 1.5), 15f, (float)maxSpd);
            }
            
            double rotate = 0;
            if (dist > 25) 
            {
                rotate = drad * 50.0;
                rotate = Mathf.Clamp((float)rotate, (float)-maxSpd, (float)maxSpd);
            }

            if (UnityEngine.Time.time - _lastMoveTime >= 0.05f)
            {
                _cubeHandle.Move(translate, rotate, 100, border: false, order: Cube.ORDER_TYPE.Weak);
                _lastMoveTime = UnityEngine.Time.time;
            }
        }
    }
}
