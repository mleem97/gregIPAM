using System;
using System.Collections.Generic;
using System.Linq;
using DHCPSwitches.Models;
using greg.Sdk.Services;

namespace DHCPSwitches;

public static class ConflictDetector
{
    public static List<IpConflict> DetectAll(List<ServerInfo> servers, List<IpSubnet> subnets)
    {
        servers ??= GregServerDiscoveryService.ScanAll();
        subnets ??= IpamEngine.GetCurrentSubnets();

        var conflicts = new List<IpConflict>();
        DetectDuplicateIp(servers, conflicts);
        DetectInvalidForCustomer(servers, conflicts);
        DetectNoIp(servers, conflicts);
        DetectVlanViolations(subnets, conflicts);
        return conflicts;
    }

    private static void DetectDuplicateIp(List<ServerInfo> servers, List<IpConflict> target)
    {
        var grouped = servers
            .Where(s => !string.IsNullOrWhiteSpace(s.Ip) && s.Ip != "0.0.0.0")
            .GroupBy(s => s.Ip, StringComparer.OrdinalIgnoreCase);

        foreach (var g in grouped)
        {
            var ids = g.Select(x => x.ServerId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
            if (ids.Count < 2)
            {
                continue;
            }

            target.Add(new IpConflict
            {
                Type = ConflictType.DuplicateIp,
                Ip = g.Key,
                AffectedServerIds = ids,
                Suggestion = "Assign unique addresses to affected servers.",
                AutoFixable = true,
            });
        }
    }

    private static void DetectInvalidForCustomer(List<ServerInfo> servers, List<IpConflict> target)
    {
        for (var i = 0; i < servers.Count; i++)
        {
            var s = servers[i];
            if (s == null || string.IsNullOrWhiteSpace(s.Ip) || s.Ip == "0.0.0.0")
            {
                continue;
            }

            if (GregCustomerService.IsIpValidForCustomer(s.CustomerId, s.Ip))
            {
                continue;
            }

            target.Add(new IpConflict
            {
                Type = ConflictType.InvalidForCustomer,
                Ip = s.Ip,
                AffectedServerIds = new List<string> { s.ServerId },
                Suggestion = "Assign an IP from the customer's usableIpsPerApp pool.",
                AutoFixable = true,
            });
        }
    }

    private static void DetectNoIp(List<ServerInfo> servers, List<IpConflict> target)
    {
        for (var i = 0; i < servers.Count; i++)
        {
            var s = servers[i];
            if (s == null || !s.IsOn)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(s.Ip) && s.Ip != "0.0.0.0")
            {
                continue;
            }

            target.Add(new IpConflict
            {
                Type = ConflictType.NoIp,
                Ip = "",
                AffectedServerIds = new List<string> { s.ServerId },
                Suggestion = "Assign DHCP or set a manual IP.",
                AutoFixable = true,
            });
        }
    }

    private static void DetectVlanViolations(List<IpSubnet> subnets, List<IpConflict> target)
    {
        var violations = VlanPolicyEngine.AnalyzeAll(subnets);
        foreach (var v in violations)
        {
            target.Add(new IpConflict
            {
                Type = v.Type,
                Ip = "",
                AffectedServerIds = new List<string>(),
                Suggestion = $"Switch={v.SwitchId} port={v.PortIndex} expected-vlan={v.ExpectedVlanId}",
                AutoFixable = true,
            });
        }
    }
}
