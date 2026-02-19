---
name: unity_mcp_codev
description: Workflow and best practices for AI agents co-developing Unity projects via UnityMCP. Focuses on handling the "Blindfolded Engineer" constraint through active exploration and verification.
---

# UnityMCP Co-Development Workflow

This skill outlines the most effective abstract process for an AI agent to implement features in Unity using the Model Context Protocol (MCP).

## Core Philosophy: The "Blindfolded Engineer"

As an AI, you cannot "see" the Unity Editor's Scene View or Inspector directly. You are a "Blindfolded Engineer" working with a partner (the User).
**Rule #1**: Never assume the state of the project. Always **query** before you **act**.

## Phase 1: Exploration (Scout)

Before writing a single line of code, build your mental model of the active scene.

*   **View Hierarchy**: `mcp_unityMCP_manage_scene(action="get_hierarchy")`
    *   *Why?* To know what GameObjects exist, their names, and parent-child relationships.
*   **Inspect Components**: `mcp_unityMCP_manage_components(action="get_components", target="...")`
    *   *Why?* To see what scripts are already attached and their current values.
*   **Check Console**: `mcp_unityMCP_read_console(action="get")`
    *   *Why?* To see if there are pre-existing errors blocking compilation.

## Phase 2: Implementation (Incremental)

Unity projects are fragile. Big-bang changes often break references.

1.  **Create Script**: `mcp_unityMCP_create_script`
    *   Start with a minimal compilable version.
2.  **Attach to GameObject**: `mcp_unityMCP_manage_components(action="add")`
    *   *Critical*: Do this *after* compilation is successful (check `refresh_unity`).
3.  **Conflict Management**:
    *   If replacing functionality (e.g., `PositionController` -> `PatrolController`), **disable** the old component (`action="set_property", enabled=false`) rather than deleting it immediately. This preserves data if you need to rollback.

## Phase 3: Verification (Runtime Truth)

Code correctness != Runtime correctness. The "Play Mode" is the source of truth.

1.  **Enter Play Mode**: `mcp_unityMCP_manage_editor(action="play")`
2.  **Verify via Logs**:
    *   Add `Debug.Log` generously in your scripts for state changes (e.g., "State Changed to X", "Connected", "Error Y").
    *   Use `mcp_unityMCP_read_console` to confirm these logs appear in order.
3.  **Exit Play Mode**: `mcp_unityMCP_manage_editor(action="stop")`
    *   *Note*: Changes made to GameObjects during Play Mode are lost. Only change scripts during Play Mode if necessary (hot reload), but usually stop first.

## Phase 4: User Communication

Since you cannot see physical hardware (e.g., toio robots, VR headsets), you must rely on the user.

*   **Bad**: "I have finished. Check it."
*   **Good**: "I have finished. Please press Play. You should see the LED turn Green. If it turns Red, tell me the error log."
*   **Ask for "Physical" Confirmation**: "Did the robot move?" "Did it stay inside the mat?"

## Summary Checklist for Agents

- [ ] Did I check the hierarchy/components *before* deciding where to attach the script?
- [ ] Did I run `refresh_unity` and check for compilation errors?
- [ ] Did I add `Debug.Log` to trace the logic flow invisible to me?
- [ ] Did I instruct the user on *how* to verify (e.g., "Press Space")?
