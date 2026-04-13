using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using DHCPSwitches.Models;
using MelonLoader;
using UnityEngine;

namespace greg.Sdk.Services;

public static class GregCustomerService
{
    public static List<CustomerBase> GetAllCustomerBases()
    {
        try
        {
            var list = UnityEngine.Object.FindObjectsOfType<CustomerBase>()?.ToList() ?? new List<CustomerBase>();
            ModLogging.Msg($"[CustomerService] Found {list.Count} CustomerBase instances");
            return list;
        }
        catch (Exception ex)
        {
            ModLogging.Warning($"[CustomerService] scan failed: {ex.Message}");
            return new List<CustomerBase>();
        }
    }

    public static CustomerBase GetByCustomerId(int customerId)
    {
        var all = GetAllCustomerBases();
        for (var i = 0; i < all.Count; i++)
        {
            var cb = all[i];
            if (cb != null && cb.customerID == customerId)
            {
                return cb;
            }
        }

        return null;
    }

    public static Dictionary<int, string> GetSubnetsPerApp(int customerId)
    {
        var cb = GetByCustomerId(customerId);
        return ConvertDictionary<int, string>(cb?.GetSubnetsPerApp());
    }

    public static Dictionary<int, int> GetVlanIdsPerApp(int customerId)
    {
        var cb = GetByCustomerId(customerId);
        return ConvertDictionary<int, int>(cb?.GetVlanIdsPerApp());
    }

    public static Dictionary<int, string[]> GetUsableIpsPerApp(int customerId)
    {
        var cb = GetByCustomerId(customerId);
        var result = new Dictionary<int, string[]>();
        if (cb == null)
        {
            return result;
        }

        try
        {
            var source = cb.usableIpsPerApp;
            if (source == null)
            {
                return result;
            }

            foreach (var entry in source)
            {
                var appId = entry.Key;
                var arr = new List<string>();
                if (entry.Value != null)
                {
                    foreach (var ip in entry.Value)
                    {
                        if (!string.IsNullOrWhiteSpace(ip))
                        {
                            arr.Add(ip);
                        }
                    }
                }

                result[appId] = arr.ToArray();
            }
        }
        catch (Exception ex)
        {
            ModLogging.Warning($"[CustomerService] usableIpsPerApp read failed: {ex.Message}");
        }

        return result;
    }

    public static Dictionary<int, int> GetAppToServerType(int customerId)
    {
        var cb = GetByCustomerId(customerId);
        return ConvertDictionary<int, int>(cb?.appIdToServerType);
    }

    public static bool IsIpValidForCustomer(int customerId, string ip)
    {
        var cb = GetByCustomerId(customerId);
        if (cb == null || string.IsNullOrWhiteSpace(ip))
        {
            return false;
        }

        try
        {
            return cb.IsIPPresent(ip);
        }
        catch
        {
            return false;
        }
    }

    public static int GetAppIdForIp(int customerId, string ip)
    {
        var cb = GetByCustomerId(customerId);
        if (cb == null || string.IsNullOrWhiteSpace(ip))
        {
            return -1;
        }

        try
        {
            return cb.GetAppIDForIP(ip);
        }
        catch
        {
            return -1;
        }
    }

    public static string GetSubnetForIp(int customerId, string ip)
    {
        var appId = GetAppIdForIp(customerId, ip);
        if (appId < 0)
        {
            return "";
        }

        var map = GetSubnetsPerApp(customerId);
        return map.TryGetValue(appId, out var cidr) ? cidr ?? "" : "";
    }

    public static int GetVlanForIp(int customerId, string ip)
    {
        var appId = GetAppIdForIp(customerId, ip);
        if (appId < 0)
        {
            return 0;
        }

        var map = GetVlanIdsPerApp(customerId);
        return map.TryGetValue(appId, out var vlan) ? vlan : 0;
    }

    public static float GetCurrentSpeed(int customerId)
    {
        return GetByCustomerId(customerId)?.currentSpeed ?? 0f;
    }

    public static float GetRequiredSpeed(int customerId)
    {
        return GetByCustomerId(customerId)?.currentTotalAppSpeeRequirements ?? 0f;
    }

    public static float GetSpeedSatisfaction(int customerId)
    {
        var req = GetRequiredSpeed(customerId);
        if (req <= 0.0001f)
        {
            return 1f;
        }

        return Mathf.Clamp01(GetCurrentSpeed(customerId) / req);
    }

    private static Dictionary<TKey, TValue> ConvertDictionary<TKey, TValue>(object src)
    {
        var result = new Dictionary<TKey, TValue>();
        if (src == null)
        {
            return result;
        }

        try
        {
            if (src is IEnumerable<KeyValuePair<TKey, TValue>> typed)
            {
                foreach (var kv in typed)
                {
                    result[kv.Key] = kv.Value;
                }

                return result;
            }

            foreach (var entry in (System.Collections.IEnumerable)src)
            {
                var t = entry.GetType();
                var k = t.GetProperty("Key")?.GetValue(entry);
                var v = t.GetProperty("Value")?.GetValue(entry);
                if (k is TKey key && v is TValue val)
                {
                    result[key] = val;
                }
            }
        }
        catch
        {
            // ignored
        }

        return result;
    }
}

public static class GregServerDiscoveryService
{
    public static List<ServerInfo> ScanAll()
    {
        var list = new List<ServerInfo>();
        Server[] servers;
        try
        {
            servers = UnityEngine.Object.FindObjectsOfType<Server>();
        }
        catch (Exception ex)
        {
            ModLogging.Warning($"[ServerDiscovery] scan failed: {ex.Message}");
            return list;
        }

        if (servers == null)
        {
            return list;
        }

        for (var i = 0; i < servers.Length; i++)
        {
            var s = servers[i];
            if (s == null)
            {
                continue;
            }

            list.Add(new ServerInfo
            {
                Instance = s.networkServer,
                ServerId = s.serverID,
                CustomerId = s.GetCustomerID(),
                Ip = GregIpService.GetIp(s.networkServer),
                ServerType = s.serverType,
                IsOn = s.isOn,
                IsBroken = s.isBroken,
                RackPositionUID = s.rackPositionUID,
            });
        }

        return list;
    }

    public static ServerInfo GetById(string serverId)
    {
        return ScanAll().FirstOrDefault(x => string.Equals(x.ServerId, serverId, StringComparison.OrdinalIgnoreCase));
    }

    public static List<ServerInfo> GetByCustomer(int customerId)
    {
        return ScanAll().Where(x => x.CustomerId == customerId).ToList();
    }

    public static List<ServerInfo> GetByRack(string rackId)
    {
        return ScanAll().Where(x => string.Equals(x.RackId, rackId, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public static List<ServerInfo> GetOnline()
    {
        return ScanAll().Where(x => x.IsOn && !x.IsBroken).ToList();
    }

    public static List<ServerInfo> GetWithoutIp()
    {
        return ScanAll().Where(x => string.IsNullOrWhiteSpace(x.Ip) || x.Ip == "0.0.0.0").ToList();
    }
}

public static class GregIpService
{
    public static string GetIp(NetworkServer sv)
    {
        if (sv == null)
        {
            return "";
        }

        try
        {
            return sv.ip ?? "";
        }
        catch
        {
            return "";
        }
    }

    public static bool HasIp(NetworkServer sv)
    {
        var ip = GetIp(sv);
        return !string.IsNullOrWhiteSpace(ip) && ip != "0.0.0.0";
    }

    public static bool SetIp(NetworkServer sv, string ip)
    {
        if (sv == null || !IsValidIp(ip))
        {
            return false;
        }

        try
        {
            sv.ip = ip;
            if (string.Equals(GetIp(sv), ip, StringComparison.Ordinal))
            {
                ModLogging.Msg("[IpService] SetIp method: direct");
                return true;
            }
        }
        catch
        {
            // ignored
        }

        try
        {
            var p = sv.GetType().GetProperty("ip", BindingFlags.Public | BindingFlags.Instance);
            p?.SetValue(sv, ip);
            if (string.Equals(GetIp(sv), ip, StringComparison.Ordinal))
            {
                ModLogging.Msg("[IpService] SetIp method: reflection");
                return true;
            }
        }
        catch
        {
            // ignored
        }

        ModLogging.Warning("[IpService] SetIp method: interop FAILED");
        return false;
    }

    public static bool IsValidIp(string ip)
    {
        return System.Net.IPAddress.TryParse(ip, out var parsed)
               && parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
    }

    public static bool IsIpInSubnet(string ip, string cidr)
    {
        if (!RouteMath.TryParseIpv4Cidr(cidr, out var networkBe, out var prefixLen))
        {
            return false;
        }

        var ipInt = (uint)IpToInt(ip);
        var mask = prefixLen == 0 ? 0u : uint.MaxValue << (32 - prefixLen);
        return (ipInt & mask) == (networkBe & mask);
    }

    public static string GetNetworkAddress(string ip, int prefix)
    {
        var cidr = $"{ip}/{Mathf.Clamp(prefix, 0, 32)}";
        if (!RouteMath.TryParseIpv4Cidr(cidr, out var networkBe, out _))
        {
            return "";
        }

        return IntToIp((int)networkBe);
    }

    public static string GetNextFreeIp(string cidr, List<string> allowedPool, List<string> usedIps, AssignMode mode)
    {
        var candidates = new List<string>();
        if (allowedPool != null && allowedPool.Count > 0)
        {
            candidates.AddRange(allowedPool.Where(IsValidIp));
        }
        else
        {
            candidates.AddRange(RouteMath.EnumerateDhcpCandidates(cidr));
        }

        if (mode == AssignMode.Random)
        {
            var rnd = new System.Random();
            candidates = candidates.OrderBy(_ => rnd.Next()).ToList();
        }
        else
        {
            candidates = candidates.OrderBy(IpToInt).ToList();
        }

        var used = new HashSet<string>(usedIps ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < candidates.Count; i++)
        {
            var ip = candidates[i];
            if (!used.Contains(ip))
            {
                return ip;
            }
        }

        return "";
    }

    public static int IpToInt(string ip)
    {
        if (!IsValidIp(ip))
        {
            return 0;
        }

        var b = System.Net.IPAddress.Parse(ip).GetAddressBytes();
        return (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];
    }

    public static string IntToIp(int ip)
    {
        return string.Format(
            "{0}.{1}.{2}.{3}",
            (ip >> 24) & 255,
            (ip >> 16) & 255,
            (ip >> 8) & 255,
            ip & 255);
    }
}

public static class GregVlanService
{
    public static bool IsVlanAllowedOnPort(NetworkSwitch sw, int portIndex, int vlanId)
    {
        try
        {
            return sw != null && sw.IsVlanAllowedOnPort(portIndex, vlanId);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsVlanAllowedOnCable(NetworkSwitch sw, CableLink cable, int vlanId)
    {
        try
        {
            return sw != null && cable != null && sw.IsVlanAllowedOnCable(cable.id, vlanId);
        }
        catch
        {
            return false;
        }
    }

    public static List<int> GetDisallowedVlans(NetworkSwitch sw, int portIndex)
    {
        var result = new List<int>();
        if (sw == null)
        {
            return result;
        }

        try
        {
            var src = sw.GetDisallowedVlans(portIndex);
            if (src != null)
            {
                foreach (var v in src)
                {
                    result.Add(v);
                }
            }
        }
        catch
        {
            // ignored
        }

        return result;
    }

    public static Dictionary<int, List<int>> GetAllPortFilters(NetworkSwitch sw)
    {
        var result = new Dictionary<int, List<int>>();
        var ports = ResolvePortCount(sw);
        for (var i = 0; i < ports; i++)
        {
            result[i] = GetDisallowedVlans(sw, i);
        }

        return result;
    }

    public static List<int> GetPortsBlockingVlan(NetworkSwitch sw, int vlanId)
    {
        return GetAllPortFilters(sw).Where(kv => kv.Value.Contains(vlanId)).Select(kv => kv.Key).ToList();
    }

    public static List<int> GetPortsAllowingVlan(NetworkSwitch sw, int vlanId)
    {
        var all = GetAllPortFilters(sw);
        return all.Where(kv => !kv.Value.Contains(vlanId)).Select(kv => kv.Key).ToList();
    }

    public static bool BlockVlan(NetworkSwitch sw, int portIndex, int vlanId)
    {
        if (sw == null)
        {
            return false;
        }

        try
        {
            var pre = string.Join(",", GetDisallowedVlans(sw, portIndex));
            ModLogging.Msg($"[VlanService] BlockVlan: sw={sw.switchId} port={portIndex} vlan={vlanId}");
            ModLogging.Msg($"[VlanService] Pre-state: {pre}");
            sw.SetVlanDisallowed(portIndex, vlanId);
            var post = string.Join(",", GetDisallowedVlans(sw, portIndex));
            ModLogging.Msg($"[VlanService] Post-state: {post}");
            return true;
        }
        catch (Exception ex)
        {
            ModLogging.Warning($"[VlanService] BlockVlan failed: {ex.Message}");
            return false;
        }
    }

    public static bool AllowVlan(NetworkSwitch sw, int portIndex, int vlanId)
    {
        if (sw == null)
        {
            return false;
        }

        try
        {
            sw.SetVlanAllowed(portIndex, vlanId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static int BlockVlanOnAllPorts(NetworkSwitch sw, int vlanId)
    {
        return BlockVlanOnPorts(sw, GetAllPortFilters(sw).Keys.ToList(), vlanId);
    }

    public static int AllowVlanOnAllPorts(NetworkSwitch sw, int vlanId)
    {
        var changed = 0;
        foreach (var port in GetAllPortFilters(sw).Keys)
        {
            if (AllowVlan(sw, port, vlanId))
            {
                changed++;
            }
        }

        return changed;
    }

    public static int BlockVlanOnPorts(NetworkSwitch sw, List<int> portIndices, int vlanId)
    {
        var changed = 0;
        if (portIndices == null)
        {
            return changed;
        }

        for (var i = 0; i < portIndices.Count; i++)
        {
            if (BlockVlan(sw, portIndices[i], vlanId))
            {
                changed++;
            }
        }

        return changed;
    }

    public static bool IsolatePortToVlan(NetworkSwitch sw, int portIndex, int vlanId, List<int> allKnownVlans)
    {
        if (sw == null)
        {
            return false;
        }

        var blocked = 0;
        var known = allKnownVlans ?? new List<int>();
        for (var i = 0; i < known.Count; i++)
        {
            var v = known[i];
            if (v <= 0 || v == vlanId)
            {
                continue;
            }

            if (BlockVlan(sw, portIndex, v))
            {
                blocked++;
            }
        }

        AllowVlan(sw, portIndex, vlanId);
        ModLogging.Msg($"[VlanService] IsolatePortToVlan: blocked {blocked} other vlans");
        return true;
    }

    public static bool ClearPortFilters(NetworkSwitch sw, int portIndex)
    {
        if (sw == null)
        {
            return false;
        }

        var list = GetDisallowedVlans(sw, portIndex);
        for (var i = 0; i < list.Count; i++)
        {
            sw.SetVlanAllowed(portIndex, list[i]);
        }

        return true;
    }

    public static int ClearAllFilters(NetworkSwitch sw)
    {
        var changed = 0;
        foreach (var p in GetAllPortFilters(sw).Keys)
        {
            if (ClearPortFilters(sw, p))
            {
                changed++;
            }
        }

        return changed;
    }

    public static bool IsVlanSupported() => true;

    public static string GetAssessmentLevel() => "FULL";

    private static int ResolvePortCount(NetworkSwitch sw)
    {
        if (sw == null)
        {
            return 0;
        }

        try
        {
            if (sw.cableLinkSwitchPorts != null)
            {
                return sw.cableLinkSwitchPorts.Count;
            }
        }
        catch
        {
            // ignored
        }

        return 0;
    }
}

public static class GregPersistenceService
{
    private static readonly object Sync = new();

    public static void Save<T>(string modName, string key, T data)
    {
        lock (Sync)
        {
            var path = BuildPath(modName, key);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            ModLogging.Msg($"[Persistence] Saved: {path}");
        }
    }

    public static T Load<T>(string modName, string key, T defaultValue)
    {
        lock (Sync)
        {
            var path = BuildPath(modName, key);
            if (!File.Exists(path))
            {
                return defaultValue;
            }

            try
            {
                var text = File.ReadAllText(path);
                return JsonSerializer.Deserialize<T>(text) ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }
    }

    public static bool Exists(string modName, string key)
    {
        lock (Sync)
        {
            return File.Exists(BuildPath(modName, key));
        }
    }

    public static void Delete(string modName, string key)
    {
        lock (Sync)
        {
            var path = BuildPath(modName, key);
            if (File.Exists(path))
            {
                File.Delete(path);
                ModLogging.Msg($"[Persistence] Deleted: {path}");
            }
        }
    }

    private static string BuildPath(string modName, string key)
    {
        var root = MelonEnvironment.UserDataDirectory;
        return Path.Combine(root, modName, key + ".json");
    }
}

public static class GregEventBus
{
    private static readonly Dictionary<Type, Dictionary<string, Delegate>> Handlers = new();

    public static void Subscribe<T>(string listenerId, Action<T> handler)
    {
        var type = typeof(T);
        if (!Handlers.TryGetValue(type, out var bucket))
        {
            bucket = new Dictionary<string, Delegate>(StringComparer.OrdinalIgnoreCase);
            Handlers[type] = bucket;
        }

        bucket[listenerId] = handler;
    }

    public static void Publish<T>(T eventData)
    {
        if (!Handlers.TryGetValue(typeof(T), out var bucket))
        {
            return;
        }

        foreach (var kv in bucket)
        {
            try
            {
                ((Action<T>)kv.Value).Invoke(eventData);
            }
            catch (Exception ex)
            {
                ModLogging.Warning($"[EventBus] handler '{kv.Key}' failed: {ex.Message}");
            }
        }
    }

    public static void Unsubscribe<T>(string listenerId)
    {
        if (Handlers.TryGetValue(typeof(T), out var bucket))
        {
            bucket.Remove(listenerId);
        }
    }

    public static void UnsubscribeAll(string listenerId)
    {
        foreach (var kv in Handlers)
        {
            kv.Value.Remove(listenerId);
        }
    }
}

public enum ToastType
{
    Success,
    Warning,
    Error,
    Info,
}

public static class GregNotificationService
{
    public static void ShowToast(string msg, ToastType type, float duration = 3f)
    {
        ModLogging.Msg($"[UI] toast {type}: {msg} ({duration:0.0}s)");
    }

    public static void ShowBanner(string msg, Color color)
    {
        ModLogging.Msg($"[UI] banner: {msg}");
    }

    public static void HideBanner()
    {
        ModLogging.Msg("[UI] banner hidden");
    }
}
