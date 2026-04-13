using System;
using System.Collections.Generic;
using System.Reflection;
using DHCPSwitches.Models;
using greg.Sdk.Services;

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
    public static List<SwitchIpEndpoint> GetSwitchEndpoints(NetworkSwitch activeSwitch)
    {
        var result = new List<SwitchIpEndpoint>();
        var servers = GregServerDiscoveryService.ScanAll();
        if (servers == null || servers.Count == 0)
        {
            return result;
        }

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
                PortIndex = -1,
                SwitchId = activeSwitch != null ? (activeSwitch.switchId ?? "") : "",
                Source = filterByRackUid ? $"rackUid={s.RackPositionUID}" : "scene-scan",
            });
        }

        return result;
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
