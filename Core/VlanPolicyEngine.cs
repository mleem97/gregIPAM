using System;
using System.Collections.Generic;
using System.Linq;
using DHCPSwitches.Models;
using greg.Sdk.Services;

namespace DHCPSwitches;

public static class VlanPolicyEngine
{
    public static VlanApplyResult ApplySubnetVlanPolicy(IpSubnet subnet)
    {
        var result = new VlanApplyResult();
        if (subnet == null || subnet.VlanId <= 0)
        {
            result.Summary = "No VLAN policy applied (subnet null or vlanId <= 0).";
            return result;
        }

        var switches = UnityEngine.Object.FindObjectsOfType<NetworkSwitch>();
        if (switches == null || switches.Length == 0)
        {
            result.Summary = "No switches found in scene.";
            return result;
        }

        var allVlans = IpamEngine.GetCurrentSubnets().Select(x => x.VlanId).Where(x => x > 0).Distinct().OrderBy(x => x).ToList();
        if (!allVlans.Contains(subnet.VlanId))
        {
            allVlans.Add(subnet.VlanId);
        }

        for (var i = 0; i < switches.Length; i++)
        {
            var sw = switches[i];
            if (sw == null)
            {
                continue;
            }

            var portCount = sw.cableLinkSwitchPorts?.Count ?? 0;
            if (portCount <= 0)
            {
                continue;
            }

            result.SwitchesAffected++;
            for (var p = 0; p < portCount; p++)
            {
                var ok = GregVlanService.IsolatePortToVlan(sw, p, subnet.VlanId, allVlans);
                if (ok)
                {
                    result.PortsModified++;
                }
                else
                {
                    result.FailedOperations++;
                }
            }
        }

        result.Summary = $"VLAN policy applied: switches={result.SwitchesAffected}, ports={result.PortsModified}, failed={result.FailedOperations}";
        return result;
    }

    public static List<VlanPolicyViolation> AnalyzeAll(List<IpSubnet> subnets)
    {
        var result = new List<VlanPolicyViolation>();
        var switches = UnityEngine.Object.FindObjectsOfType<NetworkSwitch>();
        if (switches == null)
        {
            return result;
        }

        var expectedVlans = (subnets ?? IpamEngine.GetCurrentSubnets())
            .Where(x => x.VlanId > 0)
            .Select(x => x.VlanId)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        foreach (var sw in switches)
        {
            if (sw == null)
            {
                continue;
            }

            var ports = GregVlanService.GetAllPortFilters(sw);
            foreach (var kv in ports)
            {
                for (var i = 0; i < expectedVlans.Count; i++)
                {
                    var vlan = expectedVlans[i];
                    if (!GregVlanService.IsVlanAllowedOnPort(sw, kv.Key, vlan))
                    {
                        result.Add(new VlanPolicyViolation
                        {
                            SwitchId = sw.switchId,
                            PortIndex = kv.Key,
                            ExpectedVlanId = vlan,
                            ActualState = "blocked",
                            Type = ConflictType.VlanNotIsolated,
                        });
                    }
                }
            }
        }

        return result;
    }
}
