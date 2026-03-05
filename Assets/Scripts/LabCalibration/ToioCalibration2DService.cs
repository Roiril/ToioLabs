using System;
using System.Collections.Generic;
using toio;
using UnityEngine;

namespace ToioLabs.LabCalibration
{
    /// <summary>
    /// Core class to handle 4-point calibration state and logic.
    /// Pure C# class based on Single Responsibility Principle.
    /// </summary>
    public class ToioCalibration2DService
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

        public CalibrationState CurrentState { get; private set; } = CalibrationState.None;
        
        private readonly List<Vector2Int> _calibrationPoints = new List<Vector2Int>();
        public IReadOnlyList<Vector2Int> CalibrationPoints => _calibrationPoints;
        
        private const int MinValidCoord = 10;

        public event Action<int, Vector2Int> OnPointRecorded; // index, point Mat(x,y)
        public event Action OnCalibrationDone;
        public event Action<CalibrationState> OnStateChanged;

        public void StartCalibration()
        {
            _calibrationPoints.Clear();
            ChangeState(CalibrationState.WaitingFL);
        }

        public void RecordPoint(Cube cube)
        {
            if (cube == null || CurrentState == CalibrationState.Done || CurrentState == CalibrationState.None) return;
            
            // Validate position is within a plausible range (not (0,0) or very low)
            if (cube.x < MinValidCoord && cube.y < MinValidCoord) return;

            int idx = _calibrationPoints.Count;
            _calibrationPoints.Add(new Vector2Int(cube.x, cube.y));
            cube.TurnLedOn(255, 255, 0, 500);

            OnPointRecorded?.Invoke(idx, new Vector2Int(cube.x, cube.y));

            AdvanceState(cube);
        }

        private void AdvanceState(Cube cube)
        {
            switch (CurrentState)
            {
                case CalibrationState.WaitingFL: ChangeState(CalibrationState.WaitingFR); break;
                case CalibrationState.WaitingFR: ChangeState(CalibrationState.WaitingBR); break;
                case CalibrationState.WaitingBR: ChangeState(CalibrationState.WaitingBL); break;
                case CalibrationState.WaitingBL: 
                    ChangeState(CalibrationState.Done);
                    cube.TurnLedOn(0, 255, 0, 1000);
                    OnCalibrationDone?.Invoke();
                    break;
            }
        }

        private void ChangeState(CalibrationState newState)
        {
            CurrentState = newState;
            OnStateChanged?.Invoke(newState);
        }
    }
}
