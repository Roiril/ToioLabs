---
name: toio_unity_dev
description: Best practices and troubleshooting guide for developing toio apps in Unity using the official SDK. Covers connection stability, sensor configuration, and movement control.
---

# Toio Development in Unity (Best Practices)

This skill provides guidelines for implementing robust toio control in Unity, based on practical debugging experience.

## 1. Robust Connection Strategy (Active Mat Detection)

Using `cubeManager.SingleConnect()` often connects to the wrong cube (e.g., a charging cube nearby) due to signal strength fluctuations. To ensure you connect to the **active cube on the mat**, use a "Multi-Connect & Filter" approach.

### Implementation Pattern
1. Connect to multiple cubes (e.g., 4).
2. Enable ID notifications for all.
3. Wait briefly for data.
4. Select the cube with valid coordinates (x != 0).
5. Disconnect others.

```csharp
// Example: Finding the active cube
var cubes = await cubeManager.MultiConnect(4);
foreach (var c in cubes) {
    // Crucial: Turn on notifications to receive coordinates
    await c.ConfigIDNotification(500, Cube.IDNotificationType.OnChanged); 
}
await UniTask.Delay(500); // Wait for BLE data

Cube targetCube = null;
foreach (var c in cubes) {
    if (c.x != 0 || c.y != 0) { // Valid position = On Mat
        targetCube = c;
        break;
    }
}
// Disconnect unused cubes...
```

## 2. Sensor Configuration (Mandatory)

**Problem**: `cube.x` / `cube.y` remains `(0,0)` even when connected.
**Cause**: The toio cube (depending on firmware/version) does NOT send coordinate data by default to save bandwidth.
**Fix**: You **MUST** explicitly call `ConfigIDNotification`.

```csharp
// Call this immediately after connection
await cube.ConfigIDNotification(
    intervalMs: 500, 
    notificationType: Cube.IDNotificationType.OnChanged
);
await cube.ConfigIDMissedNotification(500); // To detect 'ID Missed' (Off mat)
```

## 3. Mat Coordinate Systems

Coordinates differ by mat type. Using the wrong range causes `ToioIDmissed` errors or out-of-bounds behavior.

| Mat Type | Description | X Range | Y Range | Notes |
| :--- | :--- | :--- | :--- | :--- |
| **toio Collection** | Sumo Ring / Play Mat | 45 - 455 | 45 - 455 | 410x410 size. Standard for samples. |
| **TMD01SS-A** | Developer Mat #1 (A4/A3) | **98 - 402** | **142 - 358** | **Much smaller range!** Be careful with waypoints. |

**Tip**: Always define a `Rect` or `Vector2` bounds in your code matching the physical mat.

## 4. API Gotchas (Sound & Configuration)

Some SDK methods have confusing naming or arguments.

*   **Sound**:
    *   ❌ `cube.PlaySoundPreset(Cube.SoundPresetId.Buzz01)` (Does not exist / Compilation error)
    *   ✅ `cube.PlayPresetSound(3)` (Use `int` ID. 3 = Error/Cancel sound)
*   **TargetMove**:
    *   Use `TargetMoveType.RotatingMove` for smooth waypoint navigation.
    *   Always handle `ToioIDmissed` in the callback to stop logic if the cube leaves the mat.

## 5. Debugging Checklist

If "it doesn't work":
1.  **LED Check**: Does the cube turn green? (Connection successful?)
2.  **Console Check**: Do you see `ID Missed`? (Mat issue or Sensor dirty)
3.  **Coordinate Check**: Log `cube.x`, `cube.y` in `Update`. If `0,0`, check `ConfigIDNotification`.
4.  **Multiple Cubes**: Are you connecting to a cube on the desk instead of the one on the floor? (See Section 1).
