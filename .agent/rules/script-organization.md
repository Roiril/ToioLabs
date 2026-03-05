# Unity Script Organization Rules

## 1. Scene-Specific vs. Common Scripts

To prevent unintended side effects when editing code, follow these directory structure rules for C# scripts in `Assets/Scripts/`:

### 1.1 Scene-Specific Scripts
If a script is fundamentally designed for, or only used by, a **single specific scene**, it MUST be placed in a directory named after that scene.

**Example:**
If `ToioUIController.cs` is only used in `LabCalibration.unity`, place it in `Assets/Scripts/LabCalibration/ToioUIController.cs`.

### 1.2 Common Scripts
If a script provides general utility, core logic, or is attached to prefabs used across **multiple scenes**, it should be placed in `Assets/Scripts/Common/` (or an appropriate feature-based subfolder like `Assets/Scripts/Core/`).

**Example:**
A generic `InputManager.cs` used in both `LabMain` and `LabCalibration` goes into `Assets/Scripts/Common/InputManager.cs`.

---

## 2. Refactoring and Editing Guidelines

- **Editing Common Scripts:** When modifying a script in a common or core folder, **always confirm which scenes depend on it**. A change made for one scene might inadvertently break another. If a change is highly specific to the current scene's requirements, consider subclassing the common script or extracting the scene-specific logic into a new script located in the scene's folder.
- **Promoting Scripts:** If a scene-specific script is later needed by another scene, refactor it to ensure it has no hard dependencies on the original scene's specific hierarchy, and then move it to the `Common/` (or feature) folder.
- **Checking Usage:** Use the `Agent`'s search capabilities (e.g., `grep_search` on `.unity` and `.prefab` files looking for the script's `guid`) to confidently determine if a script is scene-specific or common before making structural changes.

---

## 3. UI Scripts

UI event receivers and visualizers often become tightly coupled to a specific canvas or scene hierarchy. 
- If the UI is a reusable prefab (e.g., a standard Button prefab used everywhere), its scripts belong in `Assets/Scripts/UI/` or `Assets/Scripts/Common/UI/`.
- If the UI is built bespoke for one scene (e.g., `CalibrationVisualizer.cs`), it should be nested under the scene folder: `Assets/Scripts/<SceneName>/UI/CalibrationVisualizer.cs`.

By strictly separating scene-specific logic from shared logic, we minimize regression bugs and keep the project workspace clean.
