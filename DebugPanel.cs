using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using static UnityEngine.Rendering.GPUSort;

public class DebugPanel : MonoBehaviour
{
    // Public configuration
    public KeyCode toggleKey = KeyCode.BackQuote; // ` key
    public bool startHidden = false;
    public Vector2 windowSize = new Vector2(600, 300);
    public static DebugPanel main;

    // UI state
    bool visible;
    Rect windowRect;
    Vector2 scroll;
    string input = "";
    List<string> lines = new List<string>();
    List<string> history = new List<string>();
    int historyIndex = -1;

    string currentFocusedControl = "";
    bool reFocusInput = false;

    // Commands
    Dictionary<string, Func<string[], string>> commands = new Dictionary<string, Func<string[], string>>(StringComparer.OrdinalIgnoreCase);

    // Runtime
    float fps;
    int frames;
    float fpsTimer;

    void Awake()
    {
        main = this;
        visible = !startHidden;
        windowRect = new Rect(10, 10, windowSize.x, windowSize.y);

        RegisterBuiltInCommands();

        // Capture Unity logs and show them in the panel
        Application.logMessageReceived += HandleLog;
        AddLine("DebugPanel initialized. Press " + toggleKey + " to toggle.");
    }

    void OnDestroy()
    {
        Application.logMessageReceived -= HandleLog;
    }

    void Update()
    {
        // Toggle visibility
        if (Input.GetKeyDown(toggleKey))
        {
            visible = !visible;
            if (visible) reFocusInput = true;
        }

        // FPS counting
        frames++;
        fpsTimer += Time.unscaledDeltaTime;
        if (fpsTimer >= 1f)
        {
            fps = frames / fpsTimer;
            frames = 0;
            fpsTimer = 0f;
        }

        //WARNING : This line assumes FirstPersonCamera exists in the project.
        FirstPersonCamera.main.lockCursor = !visible;

        // Command history navigation
        if (visible && currentFocusedControl == "DebugInput")
        {
            Cursor.lockState = CursorLockMode.None;
            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                if (history.Count > 0)
                {
                    historyIndex = Mathf.Clamp(historyIndex + 1, 0, history.Count - 1);
                    input = history[history.Count - 1 - historyIndex];
                }
            }
            else if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                if (history.Count > 0)
                {
                    historyIndex = Mathf.Clamp(historyIndex - 1, -1, history.Count - 1);
                    input = historyIndex == -1 ? "" : history[history.Count - 1 - historyIndex];
                }
            }
        }
        currentFocusedControl = "";
    }

    void OnGUI()
    {
        if (!visible) return;
        windowRect = GUI.Window(123456, windowRect, DoWindow, "Debug Panel");
    }

    void DoWindow(int id)
    {
        GUILayout.BeginVertical();

        // Top controls
        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Clear", GUILayout.Width(60))) Clear();

        if (GUILayout.Button("Copy", GUILayout.Width(60))) GUIUtility.systemCopyBuffer = string.Join("\n", lines);

        if (GUILayout.Button("Hide", GUILayout.Width(60))) visible = false;
        GUILayout.Label($"FPS: {fps:F1}");
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        // Output area
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
        GUILayout.BeginVertical();

        for (int i = 0; i < lines.Count; i++)
        {
            GUILayout.Label(lines[i]);
        }

        GUILayout.EndVertical();
        GUILayout.EndScrollView();

        // Input area
        GUILayout.BeginHorizontal();

        GUI.SetNextControlName("DebugInput");
        input = GUILayout.TextField(input, GUILayout.ExpandWidth(true));

        // Focuses input field when panel is opened
        if (reFocusInput)
        {
            GUI.FocusControl("DebugInput");
        }
        reFocusInput = false;

        if (GUILayout.Button("Send", GUILayout.Width(60)))
        {
            OnSubmit(input);
            GUI.FocusControl(null);
        }

        GUILayout.EndHorizontal();

        // Allow pressing Enter to submit (works when focused)
        currentFocusedControl = GUI.GetNameOfFocusedControl();

        if (Event.current.isKey && Event.current.keyCode == KeyCode.Return && GUI.GetNameOfFocusedControl() == "DebugInput")
        {
            OnSubmit(input);
            Event.current.Use();
        }

        // Make window draggable
        GUI.DragWindow(new Rect(0, 0, 10000, 20));
        GUILayout.EndVertical();
    }

    void OnSubmit(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;

        // Echo the full raw input once
        AddLine("> " + raw);
        // keep the raw entry in history
        history.Add(raw);
        historyIndex = -1;
        input = "";

        // Split into multiple commands using ';' as separator (honors quoted sections)
        var commandsToRun = SplitCommands(raw);
        foreach (var cmdRaw in commandsToRun)
        {
            var trimmed = cmdRaw.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            try
            {
                var args = Tokenize(trimmed);
                if (args.Length == 0) continue;

                var cmd = args[0];
                var cmdArgs = new string[args.Length - 1];
                Array.Copy(args, 1, cmdArgs, 0, cmdArgs.Length);

                if (commands.TryGetValue(cmd, out var handler))
                {
                    var result = handler(cmdArgs);
                    if (!string.IsNullOrEmpty(result))
                        AddLine(result);
                }
                else
                {
                    AddLine($"Unknown command: {cmd}. Type 'help' for a list.");
                }
            }
            catch (Exception ex)
            {
                AddLine($"Command error: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Auto-scroll to bottom
        scroll.y = float.MaxValue;
    }

    void AddLine(string text)
    {
        lines.Add($"[{DateTime.Now:HH:mm:ss}] {text}");
        // Keep the buffer bounded
        const int max = 500;
        if (lines.Count > max)
            lines.RemoveRange(0, lines.Count - max);
    }

    void Clear()
    {
        lines.Clear();
    }

    void HandleLog(string logString, string stackTrace, LogType type)
    {
        var prefix = type.ToString().ToUpper();
        AddLine($"{prefix}: {logString}");
        if (type == LogType.Error || type == LogType.Exception)
            AddLine(stackTrace);
    }

    // Basic tokenizer that supports quoted strings
    string[] Tokenize(string input)
    {
        var matches = Regex.Matches(input, @"[\""].+?[\""]|[^ ]+");
        var parts = new List<string>();
        foreach (Match m in matches)
        {
            var s = m.Value;
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
                s = s.Substring(1, s.Length - 2);
            parts.Add(s);
        }
        return parts.ToArray();
    }

    // Split input into commands separated by ';' but ignore separators inside quotes.
    IEnumerable<string> SplitCommands(string input)
    {
        var results = new List<string>();
        if (string.IsNullOrEmpty(input)) return results;

        var sb = new StringBuilder();
        bool inQuote = false;
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (c == '"')
            {
                inQuote = !inQuote;
                sb.Append(c);
            }
            else if (c == ';' && !inQuote)
            {
                results.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        if (sb.Length > 0) results.Add(sb.ToString());
        return results;
    }

    void RegisterBuiltInCommands()
    {
        commands["help"] = args =>
        {
            var available = string.Join(", ", commands.Keys);
            return $"Available commands: {available}";
        };

        commands["clear"] = args =>
        {
            Clear();
            return "Cleared panel.";
        };

        commands["time"] = args =>
        {
            return DateTime.Now.ToString("o");
        };

        commands["fps"] = args =>
        {
            return $"FPS: {fps:F1}";
        };

        commands["history"] = args =>
        {
            if (history.Count == 0) return "No commands in history.";
            return string.Join("\n", history);
        };

        // Provides the name, active state, and scene of a GameObject
        commands["find"] = args =>
        {
            if (args.Length == 0) return "Usage: find <name>";
            var name = args[0];
            var go = GameObject.Find(name);
            if (go == null) return $"GameObject '{name}' not found.";
            return $"Found '{go.name}' (active: {go.activeInHierarchy}, scene: {go.scene.name})";
        };

        // Moves a GameObject to specified coordinates
        commands["move"] = args =>
        {
            if (args.Length < 4) return "Usage: move <name> <x> <y> <z>";
            var name = args[0];
            var go = GameObject.Find(name);

            if (go == null) return $"GameObject '{name}' not found.";
            Vector3 moveTo = Vector3FromArguments(args, 1);
            Vector3 prevPos = go.transform.position;
            go.transform.position = moveTo;
            return $"'{go.name}' moved from {prevPos} to {moveTo}";
        };

        // Moves a GameObject to specified coordinates starting from another gameobject
        commands["moverelative"] = args =>
        {
            if (args.Length < 5) return "Usage: moverelative <objecttomove> <objecttomoverelativeto> <x> <y> <z>";

            var name = args[0];
            var go = GameObject.Find(name);
            if (go == null) return $"GameObject '{name}' not found.";

            name = args[1];
            var goR = GameObject.Find(name);
            if (goR == null) return $"GameObject '{name}' not found.";

            Vector3 moveTo = Vector3FromArguments(args, 2);
            Vector3 prevPos = go.transform.position;
            go.transform.position = goR.transform.position + moveTo;
            return $"'{go.name}' moved from {prevPos} to {go.transform.position}";
        };

        commands["scale"] = args =>
        {
            if (args.Length < 4) return "Usage: scale <name> <x> <y> <z>";
            var name = args[0];
            var go = GameObject.Find(name);

            if (go == null) return $"GameObject '{name}' not found.";
            Vector3 moveTo = Vector3FromArguments(args, 1);
            Vector3 prevPos = go.transform.localScale;
            go.transform.localScale = moveTo;
            return $"'{go.name}' scale changed from {prevPos} to {moveTo}";
        };

        commands["rotate"] = args =>
        {
            if (args.Length < 4) return "Usage: rotate <name> <x> <y> <z>";
            var name = args[0];
            var go = GameObject.Find(name);

            if (go == null) return $"GameObject '{name}' not found.";
            Vector3 moveTo = Vector3FromArguments(args, 1);
            Vector3 prevPos = go.transform.rotation.eulerAngles;
            go.transform.rotation = Quaternion.Euler(moveTo);
            return $"'{go.name}' rotation changed from {prevPos} to {moveTo}";
        };

        // Returns information about the GameObject under the mouse cursor
        commands["whatisthis"] = args =>
        {
            if (Camera.main == null) return "No main camera found.";
            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit, 1000f))
                return "No GameObject under cursor.";

            var go = hit.collider != null ? hit.collider.gameObject : null;
            if (go == null) return "No GameObject under cursor.";

            var comps = string.Join(", ", Array.ConvertAll(go.GetComponents<Component>(), c => c.GetType().Name));
            return $"'{go.name}' (tag: {go.tag}, layer: {LayerMask.LayerToName(go.layer)}, active: {go.activeInHierarchy})\n" +
                   $"Position: {go.transform.position}, HitPoint: {hit.point}\n" +
                   $"Components: {comps}";
        };

        commands["objectdetails"] = args =>
        {
            if(args.Length == 0) return "Usage: objectdetails <name>";
            var go = GameObject.Find(args[0]);
            if (go == null) return $"GameObject '{args[0]}' not found.";
            return $"'{go.name}' (tag: {go.tag}, layer: {LayerMask.LayerToName(go.layer)}, active: {go.activeInHierarchy})\n" +
                   $"Position: {go.transform.position}, Rotation: {go.transform.rotation.eulerAngles}, Scale: {go.transform.localScale}\n" +
                   $"Components: {string.Join(", ", Array.ConvertAll(go.GetComponents<Component>(), c => c.GetType().Name))}";
        };

        commands["timescale"] = args =>
        {
            if(args.Length == 0) return $"Current TimeScale: {Time.timeScale}";
            Time.timeScale = float.Parse(args[0]);
            return $"Set Current Timescale to {args[0]}";
        };

        commands["spawncube"] = args =>
        {
            if (args.Length == 0) return "Usage: spawncube <name> optional: <layer name>";
            var spawnpoint = Vector3.zero;
            var ray = Camera.main.transform.forward;
            if (Physics.Raycast(Camera.main.transform.position, ray, out var hit, 1000f))
            {
                spawnpoint = hit.point;
            }

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);

            cube.transform.position = spawnpoint;
            cube.name = args[0];
            

            if (args.Length > 1 && LayerMask.NameToLayer(args[1]) != -1)
            {
                cube.layer = LayerMask.NameToLayer(args[1]);
                return $"Cube '{args[0]}' created at {spawnpoint}' with layer set to '{args[1]}'.";
            }
            else if (args.Length > 1)
            {
                return $"Cube '{args[0]}' created at {spawnpoint}' however the layer was not set because '{args[1]}' was an invalid layer.";
            }

            return $"Cube '{args[0]}' created at {spawnpoint}'.";
        };

        commands["spawnsphere"] = args =>
        {
            if (args.Length == 0) return "Usage: spawncube <name> optional: <layer name>";
            var spawnpoint = Vector3.zero;
            var ray = Camera.main.transform.forward;
            if (Physics.Raycast(Camera.main.transform.position, ray, out var hit, 1000f))
            {
                spawnpoint = hit.point;
            }

            var cube = GameObject.CreatePrimitive(PrimitiveType.Sphere);

            cube.transform.position = spawnpoint;
            cube.name = args[0];


            if (args.Length > 1 && LayerMask.NameToLayer(args[1]) != -1)
            {
                cube.layer = LayerMask.NameToLayer(args[1]);
                return $"Sphere '{args[0]}' created at {spawnpoint}' with layer set to '{args[1]}'.";
            }
            else if (args.Length > 1)
            {
                return $"Sphere '{args[0]}' created at {spawnpoint}' however the layer was not set because '{args[1]}' was an invalid layer.";
            }

            return $"Sphere '{args[0]}' created at {spawnpoint}'.";
        };

        commands["spawncapsule"] = args =>
        {
            if (args.Length == 0) return "Usage: spawncube <name> optional: <layer name>";
            var spawnpoint = Vector3.zero;
            var ray = Camera.main.transform.forward;
            if (Physics.Raycast(Camera.main.transform.position, ray, out var hit, 1000f))
            {
                spawnpoint = hit.point;
            }

            var cube = GameObject.CreatePrimitive(PrimitiveType.Capsule);

            cube.transform.position = spawnpoint;
            cube.name = args[0];


            if (args.Length > 1 && LayerMask.NameToLayer(args[1]) != -1)
            {
                cube.layer = LayerMask.NameToLayer(args[1]);
                return $"Capsule '{args[0]}' created at {spawnpoint}' with layer set to '{args[1]}'.";
            }
            else if (args.Length > 1)
            {
                return $"Capsule '{args[0]}' created at {spawnpoint}' however the layer was not set because '{args[1]}' was an invalid layer.";
            }

            return $"Capsule '{args[0]}' created at {spawnpoint}'.";
        };

        commands["destroy"] = args =>
        {
            if (args.Length == 0) return "Usage: destroy <name>";
            var go = GameObject.Find(args[0]);

            if (go == null) return $"GameObject '{args[0]}' not found.";
            Destroy(go);
            return $"Gameobject '{args[0]}' was destroyed";
        };

        // You can add more commands at runtime by calling RegisterCommand

        // Base Command for copying

        commands["ping"] = args =>
        {
            return "pong";
        };
        
    }

    // Public API: allow other scripts to register commands
    public void RegisterCommand(string name, Func<string[], string> handler)
    {
        if (string.IsNullOrWhiteSpace(name) || handler == null) throw new ArgumentException(nameof(name));
        commands[name] = handler;
    }

    // Public API: write to the debug panel programmatically
    public void Write(string text)
    {
        AddLine(text);
    }

    //Utilities

    /// <summary>
    ///     Takes a list of string arguments and converts them to a Vector3 starting from start.
    /// </summary>
    public static Vector3 Vector3FromArguments(string[] args, int start)
    {
        if (args.Length == 0 || args.Length - start < 3)
        {
            DebugPanel.main.Write("Requested arguments exceeds list range");
            return Vector3.zero;
        }
        return new Vector3(float.Parse(args[start]), float.Parse(args[start+1]), float.Parse(args[start+2]));
    }
}
