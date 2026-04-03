using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace DHCPSwitches;

/// <summary>
/// Uses the game's subnet + usable-host list so we do not assign 192.168.1.x on contracts that use other CIDRs
/// (e.g. 59.107.15.0/27). <see cref="SetIP.GetUsableIPsFromSubnet"/> is the same source as the in-game IP keypad.
/// </summary>
internal static class GameSubnetHelper
{
    private static Dictionary<int, CustomerBase> _customersById;
    private static MainGameManager _cachedMgm;
    private static bool _sceneCachesReady;

    /// <summary>Drop cached scene lookups (e.g. after bulk DHCP). Next access rebuilds.</summary>
    internal static void InvalidateSceneCaches()
    {
        _sceneCachesReady = false;
        _customersById = null;
        _cachedMgm = null;
    }

    /// <summary>Rebuild customer + MainGameManager caches. Call from IPAM list tick — not from OnGUI per row.</summary>
    internal static void RefreshSceneCaches()
    {
        var arr = UnityEngine.Object.FindObjectsOfType<CustomerBase>();
        var d = new Dictionary<int, CustomerBase>(arr != null ? arr.Length : 4);
        if (arr != null)
        {
            foreach (var cb in arr)
            {
                if (cb == null)
                {
                    continue;
                }

                var id = cb.customerID;
                if (!d.TryGetValue(id, out var existing))
                {
                    d[id] = cb;
                    continue;
                }

                // Duplicate customerID (often 0 for unset + real contract): first FindObjectsOfType winner was wrong.
                if (PreferCustomerBaseForCache(cb, existing))
                {
                    ModDebugLog.Trace(
                        "subnet",
                        $"customerID={id}: using richer CustomerBase for cache (duplicate id; was {existing?.name}, now {cb?.name})");
                    d[id] = cb;
                }
            }
        }

        _customersById = d;

        var mgms = UnityEngine.Object.FindObjectsOfType<MainGameManager>();
        _cachedMgm = mgms != null && mgms.Length > 0 ? mgms[0] : null;
        _sceneCachesReady = true;
    }

    private static void EnsureSceneCaches()
    {
        if (!_sceneCachesReady)
        {
            RefreshSceneCaches();
        }
    }

    private static int SubnetMapEntryCount(CustomerBase cb)
    {
        if (cb == null)
        {
            return 0;
        }

        try
        {
            var m = cb.subnetsPerApp;
            return m == null ? 0 : m.Count;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>When two <see cref="CustomerBase"/> share <c>customerID</c>, keep the one that actually carries contract subnets (fixes id=0 placeholder vs real customer).</summary>
    private static bool PreferCustomerBaseForCache(CustomerBase candidate, CustomerBase incumbent)
    {
        if (incumbent == null)
        {
            return true;
        }

        var cN = SubnetMapEntryCount(candidate);
        var iN = SubnetMapEntryCount(incumbent);
        if (cN > iN)
        {
            return true;
        }

        if (cN < iN)
        {
            return false;
        }

        try
        {
            var incName = incumbent.customerItem != null ? incumbent.customerItem.customerName : null;
            var candName = candidate.customerItem != null ? candidate.customerItem.customerName : null;
            var incEmpty = string.IsNullOrWhiteSpace(incName);
            var candEmpty = string.IsNullOrWhiteSpace(candName);
            if (incEmpty && !candEmpty)
            {
                return true;
            }

            if (!incEmpty && candEmpty)
            {
                return false;
            }
        }
        catch
        {
            // Il2Cpp
        }

        return false;
    }

    internal static CustomerBase FindCustomerBaseForServer(Server server)
    {
        if (server == null)
        {
            return null;
        }

        EnsureSceneCaches();
        var wantCid = server.GetCustomerID();
        return _customersById != null && _customersById.TryGetValue(wantCid, out var cb) ? cb : null;
    }

    /// <summary>Display name from <see cref="CustomerItem.customerName"/> when the server’s customer base is in the scene.</summary>
    internal static string GetCustomerDisplayName(Server server)
    {
        if (server == null)
        {
            return "—";
        }

        var cb = FindCustomerBaseForServer(server);
        if (cb == null)
        {
            return "—";
        }

        var item = cb.customerItem;
        if (item == null)
        {
            return "—";
        }

        var n = item.customerName;
        return string.IsNullOrWhiteSpace(n) ? "—" : n.Trim();
    }

    internal static bool TryGetSubnetCidrForServer(Server server, out string subnetCidr)
    {
        subnetCidr = null;
        var cb = FindCustomerBaseForServer(server);
        if (cb == null)
        {
            return false;
        }

        var map = cb.subnetsPerApp;
        if (map == null)
        {
            return false;
        }

        var appId = server.appID;
        if (map.ContainsKey(appId))
        {
            subnetCidr = map[appId];
            return !string.IsNullOrWhiteSpace(subnetCidr);
        }

        // Map key mismatch (clone / app id drift): infer from another server on the same app + customer.
        var cid = server.GetCustomerID();
        foreach (var peer in UnityEngine.Object.FindObjectsOfType<Server>())
        {
            if (peer == null || peer == server)
            {
                continue;
            }

            if (peer.GetCustomerID() != cid || peer.appID != appId)
            {
                continue;
            }

            var pip = DHCPManager.GetServerIP(peer);
            if (string.IsNullOrWhiteSpace(pip) || pip == "0.0.0.0")
            {
                continue;
            }

            if (TryFindCidrWhoseUsableListContainsIp(cb, pip, out subnetCidr))
            {
                return true;
            }
        }

        // Single-app contract: only one row in subnetsPerApp.
        if (TryGetSoleSubnetCidrFromMap(cb, out subnetCidr))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Ordered CIDRs to try for DHCP: app-specific first, peer-inferred, then every subnet on the contract.
    /// Avoids falling back to the mod legacy 192.168.1.x pool when <see cref="Server.appID"/> does not match map keys.
    /// </summary>
    internal static List<string> GetSubnetCidrDhcpCandidates(Server server, CustomerBase cb)
    {
        var result = new List<string>();
        void TryAdd(string c)
        {
            if (string.IsNullOrWhiteSpace(c) || result.Contains(c))
            {
                return;
            }

            result.Add(c);
        }

        var map = cb != null ? cb.subnetsPerApp : null;
        if (map == null)
        {
            return result;
        }

        var appId = server.appID;
        if (map.ContainsKey(appId))
        {
            TryAdd(map[appId]);
        }

        var cid = server.GetCustomerID();
        foreach (var peer in UnityEngine.Object.FindObjectsOfType<Server>())
        {
            if (peer == null || peer == server)
            {
                continue;
            }

            if (peer.GetCustomerID() != cid || peer.appID != appId)
            {
                continue;
            }

            var pip = DHCPManager.GetServerIP(peer);
            if (string.IsNullOrWhiteSpace(pip) || pip == "0.0.0.0")
            {
                continue;
            }

            if (TryFindCidrWhoseUsableListContainsIp(cb, pip, out var inferred))
            {
                TryAdd(inferred);
            }
        }

        // Same customer, any app — helps when appID does not match subnetsPerApp keys (clones / drift).
        foreach (var peer in UnityEngine.Object.FindObjectsOfType<Server>())
        {
            if (peer == null || peer == server)
            {
                continue;
            }

            if (peer.GetCustomerID() != cid)
            {
                continue;
            }

            var pip = DHCPManager.GetServerIP(peer);
            if (string.IsNullOrWhiteSpace(pip) || pip == "0.0.0.0")
            {
                continue;
            }

            if (TryFindCidrWhoseUsableListContainsIp(cb, pip, out var anyAppCidr))
            {
                TryAdd(anyAppCidr);
            }
        }

        foreach (var key in map.Keys)
        {
            TryAdd(map[key]);
        }

        return result;
    }

    private static bool TryGetSoleSubnetCidrFromMap(CustomerBase cb, out string subnetCidr)
    {
        subnetCidr = null;
        var map = cb != null ? cb.subnetsPerApp : null;
        if (map == null || map.Count != 1)
        {
            return false;
        }

        foreach (var key in map.Keys)
        {
            subnetCidr = map[key];
            return !string.IsNullOrWhiteSpace(subnetCidr);
        }

        return false;
    }

    private static bool TryFindCidrWhoseUsableListContainsIp(CustomerBase cb, string ip, out string subnetCidr)
    {
        subnetCidr = null;
        var map = cb != null ? cb.subnetsPerApp : null;
        if (map == null || string.IsNullOrWhiteSpace(ip))
        {
            return false;
        }

        foreach (var key in map.Keys)
        {
            var cidr = map[key];
            if (string.IsNullOrWhiteSpace(cidr))
            {
                continue;
            }

            var arr = GetUsableIpsForSubnet(cidr);
            if (arr == null)
            {
                continue;
            }

            for (var i = 0; i < arr.Length; i++)
            {
                if (arr[i] == ip)
                {
                    subnetCidr = cidr;
                    return true;
                }
            }
        }

        return false;
    }

    internal static MainGameManager FindMainGameManager()
    {
        EnsureSceneCaches();
        return _cachedMgm;
    }

    internal static Il2CppStringArray GetUsableIpsForSubnet(string subnetCidr)
    {
        if (string.IsNullOrWhiteSpace(subnetCidr))
        {
            return null;
        }

        var mgm = FindMainGameManager();
        var setIp = mgm != null ? mgm.setIP : null;
        if (setIp == null)
        {
            return null;
        }

        return setIp.GetUsableIPsFromSubnet(subnetCidr);
    }

    /// <summary>
    /// Returns false if we resolved a usable list and the IP is not in it (game will often throw or misbehave).
    /// </summary>
    internal static bool IsIpAllowedForServer(Server server, string ip)
    {
        if (string.IsNullOrWhiteSpace(ip) || ip == "0.0.0.0")
        {
            return true;
        }

        if (CustomerPrivateSubnetRegistry.IpBelongsToCustomerPrivateLan(server, ip))
        {
            return true;
        }

        if (!TryGetSubnetCidrForServer(server, out var cidr))
        {
            return true;
        }

        var arr = GetUsableIpsForSubnet(cidr);
        if (arr == null || arr.Length == 0)
        {
            return true;
        }

        for (var i = 0; i < arr.Length; i++)
        {
            if (arr[i] == ip)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Ping targets like the contract gateway (e.g. <c>192.20.18.1</c> on <c>192.20.18.0/28</c>) are not on a <see cref="Server"/>
    /// or mod router interface — resolve them to the scene <see cref="CustomerBase"/> when the IP is in the game usable list or the usual first-host gateway.
    /// </summary>
    internal static bool TryResolveCustomerContractPingTarget(string ip, out Transform target, out string label)
    {
        target = null;
        label = null;
        if (string.IsNullOrWhiteSpace(ip))
        {
            return false;
        }

        var trimmed = ip.Trim();
        if (!RouteMath.TryParseIpv4(trimmed, out var ipU))
        {
            return false;
        }

        var customers = Resources.FindObjectsOfTypeAll<CustomerBase>();
        if (customers == null)
        {
            return false;
        }

        foreach (var cb in customers)
        {
            if (cb == null)
            {
                continue;
            }

            var go = cb.gameObject;
            if (!go.scene.IsValid() || !go.scene.isLoaded)
            {
                continue;
            }

            var map = cb.subnetsPerApp;
            if (map == null)
            {
                continue;
            }

            foreach (var key in map.Keys)
            {
                var cidr = map[key];
                if (string.IsNullOrWhiteSpace(cidr))
                {
                    continue;
                }

                if (!RouteMath.TryParsePrefix(cidr, out var net, out var pl))
                {
                    continue;
                }

                if (!RouteMath.PrefixCovers(net, pl, ipU))
                {
                    continue;
                }

                var matched = false;
                var usable = GetUsableIpsForSubnet(cidr);
                if (usable != null)
                {
                    for (var i = 0; i < usable.Length; i++)
                    {
                        var u = usable[i];
                        if (u != null && string.Equals(u.Trim(), trimmed, System.StringComparison.OrdinalIgnoreCase))
                        {
                            matched = true;
                            break;
                        }
                    }
                }

                if (!matched && pl >= 8 && pl <= 30)
                {
                    var firstHost = net + 1u;
                    if (ipU == firstHost)
                    {
                        matched = true;
                    }
                }

                if (!matched)
                {
                    continue;
                }

                target = cb.transform;
                label = FormatCustomerPingLabel(cb);
                return true;
            }
        }

        return false;
    }

    private static string FormatCustomerPingLabel(CustomerBase cb)
    {
        if (cb == null)
        {
            return "customer";
        }

        try
        {
            var item = cb.customerItem;
            var n = item != null ? item.customerName : null;
            if (!string.IsNullOrWhiteSpace(n))
            {
                return $"{n.Trim()} (contract)";
            }
        }
        catch
        {
            // Il2Cpp
        }

        return $"Customer#{cb.customerID} (contract)";
    }
}
