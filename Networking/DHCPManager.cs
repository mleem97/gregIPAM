using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace DHCPSwitches;

public static class DHCPManager
{
    private const string SUBNET_BASE = "192.168.1.";
    private const int POOL_START = 10;
    private const int POOL_END = 254;

    private static readonly HashSet<string> AssignedIPs = new();

    /// <summary>
    /// When true (default), empty <c>SetIP</c> from the game is auto-filled via Harmony (DHCP-style).
    /// Turn off from IPAM toolbar to stop background auto-assignment after clearing addresses.
    /// </summary>
    public static bool EmptyIpAutoFillEnabled { get; set; } = true;

    /// <summary>Skipped for the next empty <c>SetIP</c> when the mod intentionally clears an address.</summary>
    internal static bool SuppressEmptyIpAutoAssign { get; private set; }

    public static bool IsFlowPaused { get; private set; }

    /// <summary>Set when <see cref="SetServerIP"/> rejects or the game throws (shown in IPAM).</summary>
    public static string LastSetIpError { get; private set; }

    public static void ClearLastSetIpError() => LastSetIpError = null;

    public static void ToggleFlow()
    {
        IsFlowPaused = !IsFlowPaused;
        ModLogging.Msg(IsFlowPaused ? "Flow paused." : "Flow running.");
        ModDebugLog.Bootstrap();
        ModDebugLog.WriteLine(
            IsFlowPaused
                ? "IPAM: sim flow PAUSED — AddAppPerformance is blocked here (no IOPS ticks through this gate; L3 checks are skipped)."
                : "IPAM: sim flow RUNNING — AddAppPerformance prefix will run (L3 enforcement applies when L3 is ON).");
    }

    public static void AssignAllServers()
    {
        if (!LicenseManager.IsDHCPUnlocked)
        {
            ModLogging.Warning("DHCP locked (toggle with Ctrl+D debug).");
            return;
        }

        var servers = UnityEngine.Object.FindObjectsOfType<Server>();
        RebuildAssignedIpsFromScene(servers);

        var assigned = 0;
        foreach (var server in servers)
        {
            var ip = GetServerIP(server);
            if (!string.IsNullOrWhiteSpace(ip) && ip != "0.0.0.0")
            {
                continue;
            }

            var newIp = GetNextFreeIpForServer(server);
            if (string.IsNullOrEmpty(newIp))
            {
                continue;
            }

            if (SetServerIP(server, newIp, skipUsableListCheck: true))
            {
                AssignedIPs.Add(newIp);
                assigned++;
            }
        }

        if (assigned > 0)
        {
            ModLogging.Msg($"DHCP: {assigned} IPs assigned.");
            IPAMOverlay.InvalidateDeviceCache();
        }
    }

    /// <summary>Assign one free usable address to a single server (toolbar / per-row DHCP auto).</summary>
    public static bool AssignDhcpToSingleServer(Server server)
    {
        if (!LicenseManager.IsDHCPUnlocked)
        {
            ModLogging.Warning("DHCP locked (toggle with Ctrl+D debug).");
            return false;
        }

        if (server == null)
        {
            return false;
        }

        RebuildAssignedIpsFromScene(UnityEngine.Object.FindObjectsOfType<Server>());
        var newIp = GetNextFreeIpForServer(server);
        if (string.IsNullOrEmpty(newIp))
        {
            LastSetIpError = "No free address in the game's usable list for this server.";
            ModLogging.Warning("DHCP: AssignDhcpToSingleServer found no free IP.");
            return false;
        }

        LastSetIpError = null;
        if (!SetServerIP(server, newIp, skipUsableListCheck: true))
        {
            return false;
        }

        IPAMOverlay.InvalidateDeviceCache();
        return true;
    }

    private static void RebuildAssignedIpsFromScene(IEnumerable<Server> servers)
    {
        AssignedIPs.Clear();
        if (servers == null)
        {
            return;
        }

        foreach (var s in servers)
        {
            if (s == null)
            {
                continue;
            }

            var existingIp = GetServerIP(s);
            if (!string.IsNullOrWhiteSpace(existingIp) && existingIp != "0.0.0.0")
            {
                AssignedIPs.Add(existingIp);
            }
        }
    }

    private static bool IsUnsetIp(string ip)
    {
        return string.IsNullOrWhiteSpace(ip) || ip == "0.0.0.0";
    }

    internal static string GetServerIP(Server server)
    {
        if (server == null)
        {
            return string.Empty;
        }

        return server.IP ?? string.Empty;
    }

    /// <param name="skipUsableListCheck">True when the address was chosen from the game's usable list (DHCP path).</param>
    /// <param name="suppressAutoAssignOnEmpty">True when clearing the address from IPAM so Harmony does not immediately assign a new IP.</param>
    internal static bool SetServerIP(
        Server server,
        string ip,
        bool skipUsableListCheck = false,
        bool suppressAutoAssignOnEmpty = false)
    {
        LastSetIpError = null;

        if (server == null)
        {
            return false;
        }

        if (!skipUsableListCheck
            && !string.IsNullOrWhiteSpace(ip)
            && ip != "0.0.0.0"
            && !GameSubnetHelper.IsIpAllowedForServer(server, ip))
        {
            LastSetIpError =
                "That IP is not in this app's usable range for the contract. Use an address from the in-game IP keypad (hint list) or DHCP auto.";
            ModLogging.Warning($"SetIP blocked: '{ip}' is not in GetUsableIPsFromSubnet for this server.");
            return false;
        }

        var prevIp = GetServerIP(server);
        var needSuppress = suppressAutoAssignOnEmpty && IsUnsetIp(ip);
        try
        {
            if (needSuppress)
            {
                SuppressEmptyIpAutoAssign = true;
            }

            server.SetIP(ip);
            if (!string.IsNullOrWhiteSpace(prevIp) && prevIp != "0.0.0.0")
            {
                AssignedIPs.Remove(prevIp);
            }

            return true;
        }
        catch (Exception ex)
        {
            LastSetIpError = "The game rejected this address; see MelonLoader's log (e.g. MelonLoader/Latest.log) for details.";
            ModLogging.Error(ex);
            return false;
        }
        finally
        {
            if (needSuppress)
            {
                SuppressEmptyIpAutoAssign = false;
            }
        }
    }

    /// <summary>Legacy pool when no customer subnet is bound (e.g. main menu).</summary>
    private static string GetNextFreeLegacyPoolIp()
    {
        for (var i = POOL_START; i <= POOL_END; i++)
        {
            var candidate = SUBNET_BASE + i;
            if (!AssignedIPs.Contains(candidate))
            {
                return candidate;
            }
        }

        ModLogging.Warning("DHCP: legacy 192.168.1.x pool exhausted.");
        return null;
    }

    /// <summary>Skips x.x.x.1 — same convention as the in-game hint (gateway is usually .1 on contract subnets).</summary>
    private static bool IsTypicalGatewayLastOctet(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return false;
        }

        var parts = ip.Trim().Split('.');
        if (parts.Length != 4 || !int.TryParse(parts[3], out var last))
        {
            return false;
        }

        return last == 1;
    }

    private static bool IsIpUsedByAnotherServer(Server self, string ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return false;
        }

        foreach (var s in UnityEngine.Object.FindObjectsOfType<Server>())
        {
            if (s == null || s == self)
            {
                continue;
            }

            var cur = GetServerIP(s);
            if (cur == ip)
            {
                return true;
            }
        }

        return false;
    }

    private static string PickFromUsableArray(Il2CppStringArray usable, Server server)
    {
        if (usable == null)
        {
            return null;
        }

        for (var i = 0; i < usable.Length; i++)
        {
            var candidate = usable[i];
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (AssignedIPs.Contains(candidate))
            {
                continue;
            }

            if (IsIpUsedByAnotherServer(server, candidate))
            {
                continue;
            }

            if (IsTypicalGatewayLastOctet(candidate))
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    private static string PickFromPrivateLan(string privateCidr, Server server)
    {
        foreach (var candidate in CustomerPrivateSubnetRegistry.EnumerateDhcpCandidates(privateCidr))
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (AssignedIPs.Contains(candidate))
            {
                continue;
            }

            if (IsIpUsedByAnotherServer(server, candidate))
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    private static string GetNextFreeIpForServer(Server server)
    {
        if (server == null)
        {
            return GetNextFreeLegacyPoolIp();
        }

        if (CustomerPrivateSubnetRegistry.TryGetPrivateLanCidrForServer(server, out var privateCidr))
        {
            var fromPrivate = PickFromPrivateLan(privateCidr, server);
            if (!string.IsNullOrEmpty(fromPrivate))
            {
                return fromPrivate;
            }
        }

        if (DeviceConfigRegistry.TryGetPreferredDhcpCidrForServer(server, out var modCidr))
        {
            var usableMod = GameSubnetHelper.GetUsableIpsForSubnet(modCidr);
            var pickedMod = PickFromUsableArray(usableMod, server);
            if (!string.IsNullOrEmpty(pickedMod))
            {
                return pickedMod;
            }
        }

        var cb = GameSubnetHelper.FindCustomerBaseForServer(server);
        if (cb?.subnetsPerApp != null)
        {
            var candidates = GameSubnetHelper.GetSubnetCidrDhcpCandidates(server, cb);
            foreach (var cidr in candidates)
            {
                if (string.IsNullOrWhiteSpace(cidr))
                {
                    continue;
                }

                var usable = GameSubnetHelper.GetUsableIpsForSubnet(cidr);
                var picked = PickFromUsableArray(usable, server);
                if (!string.IsNullOrEmpty(picked))
                {
                    return picked;
                }
            }

            ModLogging.Warning(
                "DHCP: no free address in any contract subnet list for this server (all may be in use or lists empty).");
            return null;
        }

        return GetNextFreeLegacyPoolIp();
    }

    [HarmonyPatch]
    public static class ServerSetIpPatch
    {
        public static MethodBase TargetMethod()
        {
            // Use typeof(Server) — avoid AccessTools.TypeByName, which scans every loaded assembly and spams
            // ReflectionTypeLoadException on IL2CPP Unity modules Harmony cannot load.
            var serverType = typeof(Server);
            return AccessTools.Method(serverType, "SetIP", new[] { typeof(string) })
                ?? AccessTools.Method(serverType, "AssignIP", new[] { typeof(string) })
                ?? AccessTools.Method(serverType, "SetAddress", new[] { typeof(string) });
        }

        public static void Prefix(object __instance, ref string _ip)
        {
            if (!LicenseManager.IsDHCPUnlocked)
            {
                return;
            }

            if (!EmptyIpAutoFillEnabled || SuppressEmptyIpAutoAssign)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(_ip) && _ip != "0.0.0.0")
            {
                return;
            }

            if (__instance is not Server server)
            {
                return;
            }

            var autoIp = GetNextFreeIpForServer(server);
            if (string.IsNullOrEmpty(autoIp))
            {
                return;
            }

            _ip = autoIp;
            AssignedIPs.Add(autoIp);
        }

        public static void Postfix(string _ip)
        {
            if (!string.IsNullOrWhiteSpace(_ip) && _ip != "0.0.0.0")
            {
                AssignedIPs.Add(_ip);
            }
        }
    }

    [HarmonyPatch]
    public static class FlowPausePatch
    {
        private static int _addAppPerformancePrefixHits;

        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(CustomerBase), "AddAppPerformance");
        }

        public static bool Prefix(CustomerBase __instance)
        {
            var hit = Interlocked.Increment(ref _addAppPerformancePrefixHits);
            if (hit == 1)
            {
                ModDebugLog.WriteLine(
                    "IOPS: first AddAppPerformance call observed — Harmony gate is active (see IPAM Pause flow / L3 / DHCPSwitches-debug.log).");
            }

            var cid = __instance != null ? __instance.customerID : -1;
            if (IsFlowPaused)
            {
                ModDebugLog.Trace("iops", "AddAppPerformance Prefix: blocked (IsFlowPaused)");
                ModDebugLog.WriteThrottledIopsDeny(
                    cid,
                    "FLOW_PAUSED: IPAM flow is paused — click Resume in IPAM so AddAppPerformance runs.");
                return false;
            }

            if (!ReachabilityService.AllowCustomerAddAppPerformance(__instance, out var denyReason))
            {
                ModDebugLog.Trace("iops", $"AddAppPerformance Prefix: blocked (ReachabilityService) {denyReason}");
                ModDebugLog.WriteThrottledIopsDeny(cid, denyReason ?? "REACHABILITY: blocked (unknown reason)");
                return false;
            }

            ModDebugLog.WriteThrottledIopsAllow(cid, ReachabilityService.SummarizeServersForCustomer(cid));
            return true;
        }
    }
}

/// <summary>
/// Registered for Il2Cpp; periodic auto-assign was removed to avoid log spam and redundant work.
/// </summary>
public class DHCPController : MonoBehaviour
{
    public DHCPController(System.IntPtr ptr)
        : base(ptr)
    {
    }
}
