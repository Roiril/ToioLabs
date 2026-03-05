using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using toio;
using UnityEngine;

namespace ToioLabs.Core
{
    /// <summary>
    /// Handles the connection lifecycle of a Toio Cube.
    /// Pure C# class based on Single Responsibility Principle.
    /// </summary>
    public class ToioConnectionManager
    {
        public CubeManager CubeManager { get; private set; }
        public Cube ConnectedCube { get; private set; }
        public bool IsConnected => ConnectedCube != null && ConnectedCube.isConnected;

        private bool _isConnecting = false;

        public ToioConnectionManager(ConnectType connectType = ConnectType.Real)
        {
            CubeManager = new CubeManager(connectType);
        }

        public async UniTask<Cube> ConnectAsync(Action<string, Color> onStatusUpdate = null, CancellationToken cancellationToken = default)
        {
            if (_isConnecting) return ConnectedCube;
            _isConnecting = true;

            while (!cancellationToken.IsCancellationRequested)
            {
                onStatusUpdate?.Invoke("Scanning...", Color.yellow);
                
                var cubes = await CubeManager.MultiConnect(1);

                if (cubes != null && cubes.Length > 0)
                {
                    ConnectedCube = cubes[0];
                    ConnectedCube.TurnLedOn(0, 0, 255, 500);
                    onStatusUpdate?.Invoke("Connected", Color.green);
                    _isConnecting = false;

                    await ConnectedCube.ConfigIDNotification(500, Cube.IDNotificationType.OnChanged);
                    await ConnectedCube.ConfigIDMissedNotification(500);

                    return ConnectedCube;
                }
                else
                {
                    onStatusUpdate?.Invoke("Failed. Retry...", Color.red);
                    await UniTask.Delay(3000, cancellationToken: cancellationToken);
                }
            }
            
            _isConnecting = false;
            return null;
        }

        public void Disconnect()
        {
            ConnectedCube = null;
        }
    }
}
