using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DHCPSwitches;

public static class SwitchCliUI
{
    private static readonly List<string> Output = new()
    {
        "gregIPAM SwitchCLI v0.1.0",
        "Type 'help' for available commands",
    };

    private static string _input = "";
    private static Vector2 _scroll;
    private static Rect _window = new(260f, 120f, 900f, 600f);

    public static bool IsVisible { get; set; }

    public static void Draw()
    {
        if (!IsVisible)
        {
            return;
        }

        _window = GUI.Window(933710, _window, DrawWindow, "gregIPAM SwitchCLI [v0.1.0]");
    }

    public static void TickInput()
    {
        if (!IsVisible)
        {
            return;
        }

        var kb = Keyboard.current;
        if (kb == null)
        {
            return;
        }

        if (kb.tabKey.wasPressedThisFrame)
        {
            var c = SwitchCli.GetCompletions(_input);
            if (c.Count == 1)
            {
                _input = c[0] + " ";
            }
            else if (c.Count > 1)
            {
                Output.Add(string.Join("  ", c));
            }
        }

        if (kb.upArrowKey.wasPressedThisFrame && SwitchCli.History.Count > 0)
        {
            SwitchCli.HistoryIdx = Mathf.Clamp(SwitchCli.HistoryIdx - 1, 0, SwitchCli.History.Count - 1);
            _input = SwitchCli.History[SwitchCli.HistoryIdx];
        }

        if (kb.downArrowKey.wasPressedThisFrame && SwitchCli.History.Count > 0)
        {
            SwitchCli.HistoryIdx = Mathf.Clamp(SwitchCli.HistoryIdx + 1, 0, SwitchCli.History.Count - 1);
            _input = SwitchCli.History[SwitchCli.HistoryIdx];
        }
    }

    private static void DrawWindow(int id)
    {
        _ = id;
        GUI.color = new Color(0.05f, 0.07f, 0.1f, 0.98f);
        GUI.Box(new Rect(8, 26, _window.width - 16, _window.height - 34), GUIContent.none);
        GUI.color = Color.white;

        var header = new Rect(16, 34, _window.width - 32, 24);
        GUI.Label(header, "F10 toggles CLI | use 'switch list' then 'switch select <id|index>'");

        var outputRect = new Rect(16, 64, _window.width - 32, _window.height - 130);
        var viewRect = new Rect(0, 0, outputRect.width - 18, Mathf.Max(outputRect.height - 8, Output.Count * 19f + 16));
        _scroll = GUI.BeginScrollView(outputRect, _scroll, viewRect);

        var y = 6f;
        for (var i = 0; i < Output.Count; i++)
        {
            GUI.Label(new Rect(6, y, viewRect.width - 12, 18), Output[i]);
            y += 18f;
        }

        GUI.EndScrollView();

        var prompt = SwitchCli.HostnameLine + " ";
        GUI.Label(new Rect(16, _window.height - 54, 190, 22), prompt);
        GUI.SetNextControlName("SwitchCliInput");
        _input = GUI.TextField(new Rect(205, _window.height - 56, _window.width - 305, 24), _input ?? "");

        if (GUI.Button(new Rect(_window.width - 90, _window.height - 56, 70, 24), "Send"))
        {
            Submit();
        }

        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return)
        {
            Submit();
            Event.current.Use();
        }

        if (GUI.Button(new Rect(_window.width - 120, 4, 28, 20), "✕"))
        {
            IsVisible = false;
        }

        GUI.DragWindow(new Rect(0, 0, _window.width - 126, 24));
    }

    private static void Submit()
    {
        var line = _input?.Trim() ?? "";
        if (line.Length == 0)
        {
            return;
        }

        Output.Add(SwitchCli.HostnameLine + " " + line);
        var outText = SwitchCli.Execute(line);
        if (string.Equals(outText, "__CLEAR__", StringComparison.Ordinal))
        {
            Output.Clear();
        }
        else if (!string.IsNullOrWhiteSpace(outText))
        {
            var lines = outText.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                Output.Add(lines[i]);
            }
        }

        _input = "";
        _scroll.y = 999999f;
    }
}
