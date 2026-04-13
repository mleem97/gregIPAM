using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DHCPSwitches;

/// <summary>Per-<see cref="NetworkSwitch"/> running config keyed by a stable ID (see <see cref="DeviceStableId"/>), restored from disk after restart.</summary>
public static class DeviceConfigRegistry
{
    private static readonly Dictionary<string, RouterRuntimeConfig> Routers = new();
    private static readonly Dictionary<string, SwitchRuntimeConfig> Switches = new();

    internal static void BootstrapLoadDisk()
    {
        DeviceConfigPersistence.LoadSeedsFromDisk();
    }

    public static RouterRuntimeConfig GetOrCreateRouter(NetworkSwitch sw, int portCount)
    {
        var key = DeviceStableId.ForNetworkSwitch(sw);
        if (!Routers.TryGetValue(key, out var cfg))
        {
            cfg = DeviceConfigPersistence.TryTakeRouterSeed(key) ?? new RouterRuntimeConfig();
            Routers[key] = cfg;
        }

        EnsureRouterDefaults(cfg, portCount);
        return cfg;
    }

    public static SwitchRuntimeConfig GetOrCreateSwitch(NetworkSwitch sw, int portCount)
    {
        var key = DeviceStableId.ForNetworkSwitch(sw);
        if (!Switches.TryGetValue(key, out var cfg))
        {
            cfg = DeviceConfigPersistence.TryTakeSwitchSeed(key) ?? new SwitchRuntimeConfig();
            if (cfg.Vlans.Count == 0)
            {
                cfg.Vlans.Add(new SwitchVlanEntry { Id = 1, Name = "default" });
            }

            Switches[key] = cfg;
        }

        EnsureSwitchDefaults(cfg, portCount);
        return cfg;
    }

    internal static bool TrySaveAllToDisk()
    {
        return DeviceConfigPersistence.TrySaveAll(Routers, Switches);
    }

    /// <summary>Replace this router's running config with factory defaults (hostname Router, all interfaces shutdown, no L3, no static routes).</summary>
    public static void EraseRouterConfig(NetworkSwitch sw, int portCount)
    {
        var key = DeviceStableId.ForNetworkSwitch(sw);
        var cfg = new RouterRuntimeConfig();
        Routers[key] = cfg;
        EnsureRouterDefaults(cfg, portCount);
    }

    /// <summary>Replace this switch's running config with factory defaults (VLAN 1, access ports reset).</summary>
    public static void EraseSwitchConfig(NetworkSwitch sw, int portCount)
    {
        var key = DeviceStableId.ForNetworkSwitch(sw);
        var cfg = new SwitchRuntimeConfig();
        cfg.Vlans.Add(new SwitchVlanEntry { Id = 1, Name = "default" });
        Switches[key] = cfg;
        EnsureSwitchDefaults(cfg, portCount);
    }

    private static void EnsureRouterDefaults(RouterRuntimeConfig cfg, int portCount)
    {
        while (cfg.Interfaces.Count < portCount)
        {
            var i = cfg.Interfaces.Count;
            cfg.Interfaces.Add(new RouterInterfaceConfig
            {
                Name = $"Gi0/{i}",
                Index = i,
                Shutdown = true,
            });
        }
    }

    private static void EnsureSwitchDefaults(SwitchRuntimeConfig cfg, int portCount)
    {
        if (cfg.Vlans == null)
        {
            cfg.Vlans = new List<SwitchVlanEntry>();
        }

        if (cfg.Vlans.Count == 0)
        {
            cfg.Vlans.Add(new SwitchVlanEntry { Id = 1, Name = "default" });
        }

        var seen = new HashSet<int>();
        cfg.Vlans = cfg.Vlans
            .Where(v => v != null && v.Id is >= 1 and <= 4094 && seen.Add(v.Id))
            .OrderBy(v => v.Id)
            .ToList();

        if (!cfg.Vlans.Any(v => v.Id == 1))
        {
            cfg.Vlans.Insert(0, new SwitchVlanEntry { Id = 1, Name = "default" });
        }

        while (cfg.Ports.Count < portCount)
        {
            var i = cfg.Ports.Count;
            cfg.Ports.Add(new SwitchPortConfig { PortIndex = i, AccessVlan = 1, NativeVlan = 1 });
        }

        for (var i = 0; i < cfg.Ports.Count; i++)
        {
            var port = cfg.Ports[i] ?? new SwitchPortConfig();
            port.PortIndex = i;

            if (port.AccessVlan < 1 || port.AccessVlan > 4094)
            {
                port.AccessVlan = 1;
            }

            if (port.NativeVlan < 1 || port.NativeVlan > 4094)
            {
                port.NativeVlan = 1;
            }

            if (string.IsNullOrWhiteSpace(port.Mode))
            {
                port.Mode = port.Trunk ? "trunk" : "access";
            }

            port.Mode = port.Mode.Trim().ToLowerInvariant();
            if (port.Mode != "access" && port.Mode != "trunk")
            {
                port.Mode = port.Trunk ? "trunk" : "access";
            }

            port.Trunk = port.Mode == "trunk";

            if (string.IsNullOrWhiteSpace(port.AllowedVlanRaw))
            {
                port.AllowedVlanRaw = port.Trunk ? "all" : port.AccessVlan.ToString();
            }

            cfg.Ports[i] = port;
        }
    }

    /// <summary>DHCP: optional first CIDR to try (mod-defined), if compatible with game generator.</summary>
    public static bool TryGetPreferredDhcpCidrForServer(Server server, out string cidr)
    {
        cidr = null;
        return false;
    }
}
