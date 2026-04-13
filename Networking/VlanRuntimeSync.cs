using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Il2Cpp;
using UnityEngine;

namespace DHCPSwitches;

/// <summary>
/// Applies persisted switch VLAN config to in-game <see cref="NetworkSwitch"/> instances.
/// Uses the game's VLAN API when available (SetVlanAllowed/SetVlanDisallowed).
/// </summary>
internal static class VlanRuntimeSync
{
    private const int TickIntervalFrames = 120;

    private static readonly Dictionary<int, string> LastAppliedFingerprintBySwitch = new();

    private static int _lastTickFrame = -1;

    private static MethodInfo _setVlanAllowedMethod;
    private static MethodInfo _setVlanDisallowedMethod;

    internal static void Tick()
    {
        var frame = Time.frameCount;
        if (_lastTickFrame == frame || frame % TickIntervalFrames != 0)
        {
            return;
        }

        _lastTickFrame = frame;

        NetworkSwitch[] switches;
        try
        {
            switches = UnityEngine.Object.FindObjectsOfType<NetworkSwitch>();
        }
        catch
        {
            return;
        }

        if (switches == null || switches.Length == 0)
        {
            return;
        }

        for (var i = 0; i < switches.Length; i++)
        {
            var sw = switches[i];
            if (sw == null)
            {
                continue;
            }

            TryApplyToSwitch(sw);
        }
    }

    private static void TryApplyToSwitch(NetworkSwitch sw)
    {
        var portCount = ResolvePortCount(sw);
        if (portCount <= 0)
        {
            return;
        }

        var cfg = DeviceConfigRegistry.GetOrCreateSwitch(sw, portCount);
        if (cfg == null)
        {
            return;
        }

        var fingerprint = BuildFingerprint(cfg, portCount);
        var instanceId = sw.GetInstanceID();
        if (LastAppliedFingerprintBySwitch.TryGetValue(instanceId, out var last) && string.Equals(last, fingerprint, StringComparison.Ordinal))
        {
            return;
        }

        if (!ResolveVlanMethods(sw.GetType(), out var setAllowed, out var setDisallowed))
        {
            return;
        }

        var vlanIds = BuildKnownVlanSet(cfg);
        for (var port = 0; port < portCount; port++)
        {
            var portCfg = port < cfg.Ports.Count ? cfg.Ports[port] : null;
            if (portCfg == null)
            {
                continue;
            }

            foreach (var vlan in vlanIds)
            {
                var allowed = IsVlanAllowedOnPort(portCfg, vlan);
                try
                {
                    if (allowed)
                    {
                        setAllowed.Invoke(sw, new object[] { port, vlan });
                    }
                    else
                    {
                        setDisallowed.Invoke(sw, new object[] { port, vlan });
                    }
                }
                catch
                {
                    return;
                }
            }
        }

        LastAppliedFingerprintBySwitch[instanceId] = fingerprint;
    }

    internal static bool IsVlanAllowedOnPort(SwitchPortConfig port, int vlanId)
    {
        if (port == null || vlanId < 1 || vlanId > 4094)
        {
            return false;
        }

        var mode = string.IsNullOrWhiteSpace(port.Mode)
            ? (port.Trunk ? "trunk" : "access")
            : port.Mode.Trim().ToLowerInvariant();

        if (mode != "trunk")
        {
            return vlanId == NormalizeVlanId(port.AccessVlan);
        }

        if (vlanId == NormalizeVlanId(port.NativeVlan))
        {
            return true;
        }

        return IsAllowedByRawList(port.AllowedVlanRaw, vlanId);
    }

    internal static bool IsAllowedByRawList(string raw, int vlanId)
    {
        if (vlanId < 1 || vlanId > 4094)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        var text = raw.Trim();
        if (string.Equals(text, "all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(text, "none", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var anyValidToken = false;
        var tokens = text.Split(',');
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i].Trim();
            if (token.Length == 0)
            {
                continue;
            }

            if (TryParseRange(token, out var start, out var end))
            {
                anyValidToken = true;
                if (vlanId >= start && vlanId <= end)
                {
                    return true;
                }

                continue;
            }

            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var one)
                && one >= 1
                && one <= 4094)
            {
                anyValidToken = true;
                if (vlanId == one)
                {
                    return true;
                }
            }
        }

        return !anyValidToken;
    }

    private static bool TryParseRange(string token, out int start, out int end)
    {
        start = 0;
        end = 0;

        var dash = token.IndexOf('-');
        if (dash <= 0 || dash >= token.Length - 1)
        {
            return false;
        }

        var a = token.Substring(0, dash).Trim();
        var b = token.Substring(dash + 1).Trim();
        if (!int.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out start)
            || !int.TryParse(b, NumberStyles.Integer, CultureInfo.InvariantCulture, out end))
        {
            return false;
        }

        if (start > end)
        {
            (start, end) = (end, start);
        }

        if (start < 1)
        {
            start = 1;
        }

        if (end > 4094)
        {
            end = 4094;
        }

        return start <= end;
    }

    private static bool ResolveVlanMethods(Type switchType, out MethodInfo setAllowed, out MethodInfo setDisallowed)
    {
        if (_setVlanAllowedMethod == null)
        {
            _setVlanAllowedMethod = switchType.GetMethod("SetVlanAllowed", BindingFlags.Instance | BindingFlags.Public);
        }

        if (_setVlanDisallowedMethod == null)
        {
            _setVlanDisallowedMethod = switchType.GetMethod("SetVlanDisallowed", BindingFlags.Instance | BindingFlags.Public);
        }

        setAllowed = _setVlanAllowedMethod;
        setDisallowed = _setVlanDisallowedMethod;

        return setAllowed != null && setDisallowed != null;
    }

    private static int ResolvePortCount(NetworkSwitch sw)
    {
        try
        {
            var type = sw.GetType();
            var field = type.GetField("cableLinkSwitchPorts", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field?.GetValue(sw) is Array arr)
            {
                return arr.Length;
            }

            var prop = type.GetProperty("cableLinkSwitchPorts", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop?.GetValue(sw) is Array arr2)
            {
                return arr2.Length;
            }
        }
        catch
        {
            // ignored
        }

        return 0;
    }

    private static int NormalizeVlanId(int vlan)
    {
        return vlan is >= 1 and <= 4094 ? vlan : 1;
    }

    private static SortedSet<int> BuildKnownVlanSet(SwitchRuntimeConfig cfg)
    {
        var set = new SortedSet<int> { 1 };
        if (cfg?.Vlans != null)
        {
            for (var i = 0; i < cfg.Vlans.Count; i++)
            {
                var id = cfg.Vlans[i]?.Id ?? 0;
                if (id is >= 1 and <= 4094)
                {
                    set.Add(id);
                }
            }
        }

        return set;
    }

    private static string BuildFingerprint(SwitchRuntimeConfig cfg, int portCount)
    {
        var vlanPart = "1";
        if (cfg?.Vlans != null && cfg.Vlans.Count > 0)
        {
            var ids = new List<int>();
            for (var i = 0; i < cfg.Vlans.Count; i++)
            {
                var id = cfg.Vlans[i]?.Id ?? 0;
                if (id is >= 1 and <= 4094)
                {
                    ids.Add(id);
                }
            }

            ids.Sort();
            vlanPart = string.Join(",", ids);
        }

        var parts = new List<string>(portCount + 1) { vlanPart };
        for (var i = 0; i < portCount; i++)
        {
            var p = i < cfg.Ports.Count ? cfg.Ports[i] : null;
            if (p == null)
            {
                parts.Add($"{i}:access:1:1:all");
                continue;
            }

            var mode = string.IsNullOrWhiteSpace(p.Mode) ? (p.Trunk ? "trunk" : "access") : p.Mode.Trim().ToLowerInvariant();
            var av = NormalizeVlanId(p.AccessVlan);
            var nv = NormalizeVlanId(p.NativeVlan);
            var raw = string.IsNullOrWhiteSpace(p.AllowedVlanRaw) ? "all" : p.AllowedVlanRaw.Trim().ToLowerInvariant();
            parts.Add($"{i}:{mode}:{av}:{nv}:{raw}");
        }

        return string.Join("|", parts);
    }
}
