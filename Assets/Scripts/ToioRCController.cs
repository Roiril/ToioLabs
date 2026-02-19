using UnityEngine;
using toio;
using System.Threading.Tasks;

public class ToioRCController : MonoBehaviour
{
    private CubeManager cubeManager;
    private Cube cube;
    private float lastMoveTime;

    async void Start()
    {
        Debug.Log("ToioRCController: Start method called.");
        try
        {
            // Force connection to Real cubes (not Simulator)
            cubeManager = new CubeManager(ConnectType.Real);
            
            Debug.Log("ToioRCController: Attempting to connect to a cube (Real Mode)...");
            // Search for cubes
            var cubes = await cubeManager.MultiConnect(1); // Try to connect to 1 cube
            
            if (cubes != null && cubes.Length > 0)
            {
                cube = cubes[0];
                Debug.Log("ToioRCController: Toio Cube Connected! ID: " + cube.id);
                
                // Turn on light to visually confirm
                cube.TurnLedOn(255, 0, 0, 0); // Red light
            }
            else
            {
                Debug.LogWarning("ToioRCController: No cubes found or connection timed out.");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("ToioRCController: Error during connection: " + e.Message);
        }
    }

    void Update()
    {
        if (cube == null) return;
        
        // ... rest of update
        float vertical = Input.GetAxis("Vertical");
        float horizontal = Input.GetAxis("Horizontal");

        // Calculate speeds
        int left = (int)Mathf.Clamp((vertical + horizontal) * 100, -100, 100);
        int right = (int)Mathf.Clamp((vertical - horizontal) * 100, -100, 100);

        // Send move command every 50ms
        if (Time.time - lastMoveTime >= 0.05f)
        {
            // Duration is set to 100ms to cover the gap until the next command
            cube.Move(left, right, 100);
            lastMoveTime = Time.time;
        }
    }
}
