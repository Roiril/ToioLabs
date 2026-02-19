using UnityEngine;
using toio;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;

public class ToioPatrolController : MonoBehaviour
{
    private CubeManager cubeManager;
    private Cube cube;
    private bool isConnected = false;

    [Header("Patrol Settings")]
    // Adjusted for Developer Mat (TMD01SS-A) Range: x[98-402] y[142-358]
    public Vector2[] waypoints = new Vector2[] 
    {
        new Vector2(150, 180),
        new Vector2(350, 180),
        new Vector2(350, 320),
        new Vector2(150, 320)
    };
    public float waitTimeSeconds = 1.0f;
    public int maxSpeed = 80;

    [Header("Current Status")]
    public int currentWaypointIndex = 0;
    public bool isPatrolling = false;
    public bool isWaiting = false;
    public string statusMessage = "Initializing...";

    // Debug View
    public int currentX;
    public int currentY;

    async void Start()
    {
        // --- 1. Robust Connection Logic (Same as ToioPositionController) ---
        cubeManager = new CubeManager(ConnectType.Real);
        Debug.Log("ToioPatrolController: Scanning for cubes...");
        statusMessage = "Scanning...";

        // Connect to up to 4 cubes to ensure we catch the one on the mat
        var cubes = await cubeManager.MultiConnect(4);

        if (cubes != null && cubes.Length > 0)
        {
            Debug.Log($"Connected to {cubes.Length} cubes. Identifying target...");
            
            // Configure Sensors for ALL cubes
            foreach (var c in cubes)
            {
                await c.ConfigIDNotification(500, Cube.IDNotificationType.OnChanged);
                await c.ConfigIDMissedNotification(500);
            }

            // Wait for sensor data
            await UniTask.Delay(1000);

            // Find the cube on the mat
            foreach (var c in cubes)
            {
                if (c.x != 0 || c.y != 0)
                {
                    cube = c;
                    break;
                }
            }

            // Fallback
            if (cube == null) 
            {
                Debug.LogWarning("No cube on mat detected. Picking first one.");
                cube = cubes[0];
            }
            else
            {
                // Disconnect others
                foreach(var c in cubes)
                {
                    if (c != cube) cubeManager.Disconnect(c);
                }
            }

            isConnected = true;
            Debug.Log($"ToioPatrolController: Ready! ID {cube.id}");
            statusMessage = "Ready (Press P to Start)";
            cube.TurnLedOn(0, 255, 0, 500); // Green flash

            // Register Callbacks
            cube.targetMoveCallback.AddListener("PatrolController", OnTargetMoveResult);
            // Debug Log for missed
             cube.idMissedCallback.AddListener("PatrolController", (c) => Debug.LogWarning("[Patrol] Mat Missed!"));
        }
        else
        {
            Debug.LogWarning("ToioPatrolController: No cubes found.");
            statusMessage = "Connection Failed";
        }
    }

    void Update()
    {
        if (!isConnected || cube == null) return;

        // Monitor Status
        currentX = cube.x;
        currentY = cube.y;

        // Input Handling
        if (Input.GetKeyDown(KeyCode.P))
        {
            TogglePatrol();
        }
    }

    void TogglePatrol()
    {
        isPatrolling = !isPatrolling;
        
        if (isPatrolling)
        {
            Debug.Log("Patrol Started");
            statusMessage = "Patrolling: Moving to WP " + currentWaypointIndex;
            MoveToNextWaypoint();
        }
        else
        {
            Debug.Log("Patrol Paused");
            statusMessage = "Paused";
            // Stop movement immediately
            cube.Move(0, 0, 0); 
        }
    }

    void MoveToNextWaypoint()
    {
        if (!isPatrolling || isWaiting || waypoints.Length == 0) return;

        Vector2 target = waypoints[currentWaypointIndex];
        
        Debug.Log($"[Patrol] Moving to WP[{currentWaypointIndex}]: {target}");
        
        cube.TargetMove(
            targetX: (int)target.x,
            targetY: (int)target.y,
            targetAngle: 0, 
            maxSpd: maxSpeed,
            targetMoveType: Cube.TargetMoveType.RotatingMove
        );
    }

    async void OnTargetMoveResult(Cube c, int configID, Cube.TargetMoveRespondType response)
    {
        // Ignore if we stopped patrolling manually
        if (!isPatrolling) return;

        if (response == Cube.TargetMoveRespondType.Normal)
        {
            Debug.Log($"[Patrol] Arrived at WP[{currentWaypointIndex}]");
            
            // Start Wait
            isWaiting = true;
            statusMessage = $"Waiting at WP {currentWaypointIndex}...";
            
            // 1 second pause
            await UniTask.Delay((int)(waitTimeSeconds * 1000));
            
            isWaiting = false;
            
            // Update Index (Loop)
            currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Length;
            
            // Check if still patrolling (user might have paused during wait)
            if (isPatrolling) 
            {
                statusMessage = "Patrolling: Moving to WP " + currentWaypointIndex;
                MoveToNextWaypoint();
            }
        }
        else if (response == Cube.TargetMoveRespondType.ToioIDmissed)
        {
            Debug.LogWarning("[Patrol] Position Lost! Stopping Patrol.");
            isPatrolling = false;
            statusMessage = "Error: Position Lost";
            cube.PlayPresetSound(3); // Error sound (ID 3 = Cancel/Error usually)
        }
        else
        {
            Debug.Log($"[Patrol] Move ended with response: {response}");
            // Optional: Retry logic could go here
        }
    }
}
