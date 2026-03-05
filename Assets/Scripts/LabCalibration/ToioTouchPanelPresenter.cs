using System;
using UnityEngine;
using toio;
using ToioLabs.Core;
using System.Threading;
using ToioLabs.UI;

namespace ToioLabs.LabCalibration
{
    /// <summary>
    /// Presenter that coordinates the View (ToioUIController) and Services (Connection, Calibration).
    /// Pure C# class.
    /// </summary>
    public class ToioTouchPanelPresenter : IDisposable
    {
        private readonly ToioUIController _view;
        private readonly ToioConnectionManager _connectionManager;
        private readonly ToioCalibration2DService _calibrationService;

        private float _lastSendTime;
        private Vector2 _lastSentMatPos;
        private float _nextBatteryUpdateTime;
        private float _nextPosUpdateTime;

        private CancellationTokenSource _cts;

        public ToioTouchPanelPresenter(ToioUIController view)
        {
            _view = view;
            
            _connectionManager = ServiceLocator.Resolve<ToioConnectionManager>();
            if (_connectionManager == null)
            {
                _connectionManager = new ToioConnectionManager(ConnectType.Real);
                ServiceLocator.Register(_connectionManager);
            }

            _calibrationService = ServiceLocator.Resolve<ToioCalibration2DService>();
            if (_calibrationService == null)
            {
                _calibrationService = new ToioCalibration2DService();
                ServiceLocator.Register(_calibrationService);
            }

            _calibrationService.OnStateChanged += HandleCalibrationStateChanged;
            _calibrationService.OnPointRecorded += HandlePointRecorded;
            _calibrationService.OnCalibrationDone += HandleCalibrationDone;

            _view.OnSpacePressed += HandleSpacePressed;
            _view.OnRecalibrateRequested += HandleRecalibrateRequested;
            _view.OnTouchInput += HandleTouchInput;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            ConnectAndCalibrateAsync().Forget();
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
                _calibrationService.OnPointRecorded -= HandlePointRecorded;
                _calibrationService.OnCalibrationDone -= HandleCalibrationDone;
            }

            if (_view != null)
            {
                _view.OnSpacePressed -= HandleSpacePressed;
                _view.OnRecalibrateRequested -= HandleRecalibrateRequested;
                _view.OnTouchInput -= HandleTouchInput;
            }
        }

        private async Cysharp.Threading.Tasks.UniTaskVoid ConnectAndCalibrateAsync()
        {
            _view.UpdateInstructions(_calibrationService.CurrentState);

            var cube = await _connectionManager.ConnectAsync(_view.UpdateConnectionStatus, _cts.Token);
            if (cube != null && !_cts.IsCancellationRequested)
            {
                cube.idCallback.AddListener("ToioTouchPanelPresenter", HandleCubeIdUpdate);
                EnterCalibration();
                
                TelemetryLoopAsync(_cts.Token).Forget();
            }
        }

        private async Cysharp.Threading.Tasks.UniTaskVoid TelemetryLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (!_connectionManager.IsConnected)
                {
                    if (_connectionManager.CubeManager != null && _connectionManager.CubeManager.cubes.Count > 0 && 
                        !_connectionManager.CubeManager.cubes[0].isConnected)
                    {
                        _connectionManager.Disconnect();
                        ConnectAndCalibrateAsync().Forget();
                        break;
                    }
                }
                else
                {
                    _view.UpdateBatteryStatus(_connectionManager.ConnectedCube.battery);
                }

                await Cysharp.Threading.Tasks.UniTask.Delay(TimeSpan.FromSeconds(_view.BatteryUpdateInterval), cancellationToken: token);
            }
        }

        private void HandleCubeIdUpdate(Cube cube)
        {
            _view.UpdateIndicatorPosition(cube.x, cube.y);
            _view.UpdatePositionStatus(cube.x, cube.y);
        }

        private void EnterCalibration()
        {
            _view.ResetForCalibration();
            _calibrationService.StartCalibration();
        }

        private void HandleSpacePressed()
        {
            _calibrationService.RecordPoint(_connectionManager.ConnectedCube);
        }

        private void HandleRecalibrateRequested()
        {
            EnterCalibration();
        }

        private void HandleCalibrationStateChanged(ToioCalibration2DService.CalibrationState state)
        {
            _view.UpdateInstructions(state);
            _view.UpdateCalibrationStatus(state, _calibrationService.CalibrationPoints.Count);
        }

        private void HandlePointRecorded(int index, Vector2Int matPos)
        {
            _view.ShowCalibrationVisualFeedback(index, matPos);
        }

        private void HandleCalibrationDone()
        {
            // Transition view to live mode
            _view.ApplyCalibrationToView();
        }

        private void HandleTouchInput(Vector2 screenPos)
        {
            if (!_connectionManager.IsConnected || _calibrationService.CurrentState != ToioCalibration2DService.CalibrationState.Done) return;

            if (_view.TryGetMatTargetPosition(screenPos, out int targetX, out int targetY))
            {
                if (UnityEngine.Time.time - _lastSendTime >= _view.SendInterval && 
                    Vector2.Distance(new Vector2(targetX, targetY), _lastSentMatPos) >= _view.MinDistance)
                {
                    MoveCube(targetX, targetY);
                }
            }
        }

        private void MoveCube(int x, int y)
        {
            _connectionManager.ConnectedCube.TargetMove(targetX: x, targetY: y, targetAngle: 0, maxSpd: 80, targetMoveType: Cube.TargetMoveType.RotatingMove);
            _lastSendTime = UnityEngine.Time.time;
            _lastSentMatPos = new Vector2(x, y);
        }

        // UpdateTelemetry removed as handling logic is now inside TelemetryLoopAsync and HandleCubeIdUpdate
    }
}
