using System;
using System.Reflection;
using UnityEngine;

namespace DHCPSwitches;

public enum NetworkDeviceKind
{
    Layer2Switch,
    Router,
}

/// <summary>
/// Classifies <see cref="NetworkSwitch"/> as L2 vs router. Model string comes from reflection (game-specific fields) with name fallback.
/// </summary>
public static class NetworkDeviceClassifier
{
    public const string RouterModelToken1 = "4xSFP";
    public const string RouterModelToken2 = "SFP28";

    private static readonly string[] ModelMemberNames =
    {
        "model",
        "Model",
        "modelName",
        "ModelName",
        "switchModel",
        "SwitchModel",
        "catalogName",
        "CatalogName",
        "deviceModel",
        "DeviceModel",
        "hardwareModel",
        "HardwareModel",
        "sku",
        "SKU",
    };

    public static NetworkDeviceKind GetKind(NetworkSwitch sw)
    {
        if (sw == null)
        {
            return NetworkDeviceKind.Layer2Switch;
        }

        var model = TryGetModelString(sw);
        if (!string.IsNullOrEmpty(model) && LooksLikeRouterModel(model))
        {
            return NetworkDeviceKind.Router;
        }

        var n = sw.name ?? string.Empty;
        if (LooksLikeRouterModel(n))
        {
            return NetworkDeviceKind.Router;
        }

        return NetworkDeviceKind.Layer2Switch;
    }

    public static string GetModelDisplay(NetworkSwitch sw)
    {
        if (sw == null)
        {
            return "";
        }

        var m = TryGetModelString(sw);
        return string.IsNullOrEmpty(m) ? "" : m.Trim();
    }

    private static bool LooksLikeRouterModel(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        return text.IndexOf(RouterModelToken1, StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf(RouterModelToken2, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string TryGetModelString(NetworkSwitch sw)
    {
        var t = sw.GetType();

        foreach (var name in ModelMemberNames)
        {
            try
            {
                var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p?.GetValue(sw) is string s && !string.IsNullOrWhiteSpace(s))
                {
                    return s;
                }

                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f?.GetValue(sw) is string s2 && !string.IsNullOrWhiteSpace(s2))
                {
                    return s2;
                }
            }
            catch
            {
                // ignore reflection failures on Il2Cpp
            }
        }

        return null;
    }

    public static int GetPortCount(NetworkSwitch sw, int fallback = 4)
    {
        if (sw == null)
        {
            return fallback;
        }

        var t = sw.GetType();
        foreach (var name in new[] { "portCount", "PortCount", "ports", "numberOfPorts", "NumberOfPorts" })
        {
            try
            {
                var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p?.GetValue(sw) is int i && i > 0)
                {
                    return i;
                }

                if (p?.GetValue(sw) is long l && l > 0)
                {
                    return (int)l;
                }

                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f?.GetValue(sw) is int i2 && i2 > 0)
                {
                    return i2;
                }
            }
            catch
            {
                // ignore
            }
        }

        return fallback;
    }
}
