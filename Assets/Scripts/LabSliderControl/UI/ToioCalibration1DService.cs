using System;
using toio;

namespace ToioLabs.UI
{
    /// <summary>
    /// Core class to handle 2-point (Right, Left) calibration state and logic.
    /// Pure C# class based on Single Responsibility Principle.
    /// </summary>
    public class ToioCalibration1DService
    {
        public enum CalibrationState
        {
            WaitingForRight,
            WaitingForLeft,
            Completed
        }

        public CalibrationState CurrentState { get; private set; } = CalibrationState.WaitingForRight;
        
        public int LeftBoundX { get; private set; }
        public int RightBoundX { get; private set; }
        public int CalibratedY { get; private set; }

        public event Action<CalibrationState> OnStateChanged;
        public event Action<string> OnError;

        public void StartCalibration(int defaultLeftBoundX = 455, int defaultRightBoundX = 45)
        {
            LeftBoundX = defaultLeftBoundX;
            RightBoundX = defaultRightBoundX;
            ChangeState(CalibrationState.WaitingForRight);
        }

        public void RecordPoint(Cube cube)
        {
            if (cube == null || CurrentState == CalibrationState.Completed) return;

            if (cube.x == 0 && cube.y == 0)
            {
                OnError?.Invoke("Please place toio on the mat!");
                return;
            }

            switch (CurrentState)
            {
                case CalibrationState.WaitingForRight:
                    RightBoundX = cube.x;
                    CalibratedY = cube.y;
                    ChangeState(CalibrationState.WaitingForLeft);
                    break;

                case CalibrationState.WaitingForLeft:
                    LeftBoundX = cube.x;
                    ChangeState(CalibrationState.Completed);
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
