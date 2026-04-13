using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DHCPSwitches.Models;
using greg.Sdk.Services;
using UnityEngine;

namespace DHCPSwitches;

public sealed class SwitchIpEndpoint
{
    public string ServerId { get; set; } = "";
    public string Ip { get; set; } = "";
    public int CustomerId { get; set; }
    public int AppId { get; set; }
    public int VlanId { get; set; }
    public int PortIndex { get; set; } = -1;
    public string SwitchId { get; set; } = "";
    public string Source { get; set; } = "";
}

public static class ServerScanAdapter
{
    private static readonly Dictionary<Type, MemberInfo[]> InspectableMembersCache = new();

    public static List<SwitchIpEndpoint> GetSwitchEndpoints(NetworkSwitch activeSwitch)
    {
        var result = new List<SwitchIpEndpoint>();
        var servers = GregServerDiscoveryService.ScanAll();
        if (servers == null || servers.Count == 0)
        {
            return result;
        }

        var portMap = BuildServerPortMap(activeSwitch, servers, out var portSourceMap);

        var filterByRackUid = TryGetSwitchRackPositionUid(activeSwitch, out var switchRackUid) && switchRackUid > 0;
        for (var i = 0; i < servers.Count; i++)
        {
            var s = servers[i];
            if (s == null || string.IsNullOrWhiteSpace(s.Ip) || s.Ip == "0.0.0.0")
            {
                continue;
            }

            if (filterByRackUid && s.RackPositionUID > 0 && s.RackPositionUID != switchRackUid)
            {
                continue;
            }

            result.Add(new SwitchIpEndpoint
            {
                ServerId = s.ServerId ?? "",
                Ip = s.Ip ?? "",
                CustomerId = s.CustomerId,
                AppId = GregCustomerService.GetAppIdForIp(s.CustomerId, s.Ip),
                VlanId = GregCustomerService.GetVlanForIp(s.CustomerId, s.Ip),
                PortIndex = portMap.TryGetValue(s.ServerId ?? "", out var p) ? p : -1,
                SwitchId = activeSwitch != null ? (activeSwitch.switchId ?? "") : "",
                Source = BuildEndpointSource(s, filterByRackUid, portSourceMap),
            });
        }

        return result;
    }

    private static string BuildEndpointSource(ServerInfo s, bool filterByRackUid, Dictionary<string, string> portSourceMap)
    {
        var sid = s?.ServerId ?? "";
        if (!string.IsNullOrWhiteSpace(sid) && portSourceMap.TryGetValue(sid, out var src) && !string.IsNullOrWhiteSpace(src))
        {
            return src;
        }

        return filterByRackUid ? $"rackUid={s?.RackPositionUID ?? 0}" : "scene-scan";
    }

    private static Dictionary<string, int> BuildServerPortMap(NetworkSwitch activeSwitch, List<ServerInfo> servers, out Dictionary<string, string> portSourceMap)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        portSourceMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (activeSwitch == null || servers == null || servers.Count == 0)
        {
            return map;
        }

        var links = activeSwitch.cableLinkSwitchPorts;
        var portCount = links?.Count ?? 0;
        if (portCount <= 0)
        {
            return map;
        }

        var serverComponents = Object.FindObjectsOfType<Server>() ?? Array.Empty<Server>();
        var byServerId = serverComponents
            .Where(x => x != null && !string.IsNullOrWhiteSpace(x.serverID))
            .GroupBy(x => x.serverID, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        for (var s = 0; s < servers.Count; s++)
        {
            var info = servers[s];
            if (info == null || string.IsNullOrWhiteSpace(info.ServerId) || map.ContainsKey(info.ServerId))
            {
                continue;
            }

            byServerId.TryGetValue(info.ServerId, out var serverComponent);
            for (var p = 0; p < portCount; p++)
            {
                var link = links[p];
                if (TryResolvePortForServer(link, info, serverComponent, out var source))
                {
                    map[info.ServerId] = p;
                    portSourceMap[info.ServerId] = source;
                    break;
                }
            }
        }

        return map;
    }

    private static bool TryResolvePortForServer(object link, ServerInfo serverInfo, Server serverComponent, out string source)
    {
        source = "";
        if (link == null || serverInfo == null)
        {
            return false;
        }

        if (!IsLikelyConnectedLink(link))
        {
            return false;
        }

        if (serverInfo.Instance != null && TryContainsObjectReference(link, serverInfo.Instance, 2, out var networkPath))
        {
            source = "link-network:" + networkPath;
            return true;
        }

        if (serverComponent != null && TryContainsObjectReference(link, serverComponent, 2, out var serverPath))
        {
            source = "link-server:" + serverPath;
            return true;
        }

        if (serverComponent != null && serverComponent.transform != null && TryContainsObjectReference(link, serverComponent.transform, 2, out var transformPath))
        {
            source = "link-transform:" + transformPath;
            return true;
        }

        if (serverComponent != null && serverComponent.gameObject != null && TryContainsObjectReference(link, serverComponent.gameObject, 2, out var goPath))
        {
            source = "link-gameobject:" + goPath;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(serverInfo.ServerId)
            && TryContainsStringValue(link, serverInfo.ServerId, 2, out var idPath))
        {
            source = "link-serverId:" + idPath;
            return true;
        }

        return false;
    }

    private static bool IsLikelyConnectedLink(object link)
    {
        if (TryGetBoolMember(link, "connected", out var connected))
        {
            return connected;
        }

        if (TryGetBoolMember(link, "isConnected", out connected))
        {
            return connected;
        }

        return true;
    }

    private static bool TryContainsObjectReference(object root, object target, int maxDepth, out string path)
    {
        path = "";
        if (root == null || target == null)
        {
            return false;
        }

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        return Traverse(root, "$", 0, maxDepth, visited, ref path, o => ReferenceEquals(o, target));
    }

    private static bool TryContainsStringValue(object root, string expected, int maxDepth, out string path)
    {
        path = "";
        if (root == null || string.IsNullOrWhiteSpace(expected))
        {
            return false;
        }

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        return Traverse(
            root,
            "$",
            0,
            maxDepth,
            visited,
            ref path,
            o => o is string s && string.Equals(s, expected, StringComparison.OrdinalIgnoreCase));
    }

    private static bool Traverse(
        object node,
        string currentPath,
        int depth,
        int maxDepth,
        HashSet<object> visited,
        ref string matchPath,
        Func<object, bool> matcher)
    {
        if (node == null)
        {
            return false;
        }

        if (matcher(node))
        {
            matchPath = currentPath;
            return true;
        }

        if (depth >= maxDepth)
        {
            return false;
        }

        var nodeType = node.GetType();
        if (IsLeafType(nodeType))
        {
            return false;
        }

        if (!visited.Add(node))
        {
            return false;
        }

        if (node is System.Collections.IEnumerable enumerable && node is not string)
        {
            var idx = 0;
            foreach (var item in enumerable)
            {
                if (Traverse(item, $"{currentPath}[{idx}]", depth + 1, maxDepth, visited, ref matchPath, matcher))
                {
                    return true;
                }

                idx++;
                if (idx >= 24)
                {
                    break;
                }
            }
        }

        var members = GetInspectableMembers(nodeType);
        for (var i = 0; i < members.Length; i++)
        {
            var member = members[i];
            if (!TryGetMemberValue(member, node, out var value))
            {
                continue;
            }

            if (Traverse(value, currentPath + "." + member.Name, depth + 1, maxDepth, visited, ref matchPath, matcher))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLeafType(Type type)
    {
        if (type == null)
        {
            return true;
        }

        return type.IsPrimitive
               || type.IsEnum
               || type == typeof(string)
               || type == typeof(decimal)
               || type == typeof(DateTime)
               || type == typeof(TimeSpan)
               || type == typeof(Guid);
    }

    private static MemberInfo[] GetInspectableMembers(Type type)
    {
        if (type == null)
        {
            return Array.Empty<MemberInfo>();
        }

        if (InspectableMembersCache.TryGetValue(type, out var cached))
        {
            return cached;
        }

        var members = new List<MemberInfo>();
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        var fields = type.GetFields(flags);
        for (var i = 0; i < fields.Length; i++)
        {
            members.Add(fields[i]);
        }

        var props = type.GetProperties(flags);
        for (var i = 0; i < props.Length; i++)
        {
            var p = props[i];
            if (!p.CanRead)
            {
                continue;
            }

            if (p.GetIndexParameters().Length != 0)
            {
                continue;
            }

            members.Add(p);
        }

        cached = members.ToArray();
        InspectableMembersCache[type] = cached;
        return cached;
    }

    private static bool TryGetBoolMember(object obj, string memberName, out bool value)
    {
        value = false;
        if (obj == null || string.IsNullOrWhiteSpace(memberName))
        {
            return false;
        }

        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = obj.GetType();

        var field = type.GetField(memberName, flags);
        if (field != null && TryGetMemberValue(field, obj, out var fVal) && fVal is bool bField)
        {
            value = bField;
            return true;
        }

        var prop = type.GetProperty(memberName, flags);
        if (prop != null && prop.CanRead && prop.GetIndexParameters().Length == 0 && TryGetMemberValue(prop, obj, out var pVal) && pVal is bool bProp)
        {
            value = bProp;
            return true;
        }

        return false;
    }

    private static bool TryGetMemberValue(MemberInfo member, object instance, out object value)
    {
        value = null;
        if (member == null || instance == null)
        {
            return false;
        }

        try
        {
            switch (member)
            {
                case FieldInfo fi:
                    value = fi.GetValue(instance);
                    return true;
                case PropertyInfo pi:
                    if (!pi.CanRead || pi.GetIndexParameters().Length != 0)
                    {
                        return false;
                    }

                    value = pi.GetValue(instance);
                    return true;
                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetSwitchRackPositionUid(NetworkSwitch sw, out int uid)
    {
        uid = 0;
        if (sw == null)
        {
            return false;
        }

        try
        {
            var type = sw.GetType();
            var field = type.GetField("rackPositionUID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field?.GetValue(sw) is int v)
            {
                uid = v;
                return true;
            }

            var prop = type.GetProperty("rackPositionUID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop?.GetValue(sw) is int p)
            {
                uid = p;
                return true;
            }
        }
        catch
        {
            // ignored
        }

        return false;
    }
}
