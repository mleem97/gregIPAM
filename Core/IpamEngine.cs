using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DHCPSwitches.Models;
using greg.Sdk.Events;
using greg.Sdk.Services;
using MelonLoader;

namespace DHCPSwitches;

public static class IpamEngine
{
    private static readonly List<IpSubnet> CurrentSubnets = new();
    private static readonly List<IpConflict> CurrentConflicts = new();

    private static bool _scanInProgress;

    public static List<IpSubnet> GetCurrentSubnets() => new(CurrentSubnets);

    public static List<IpConflict> GetCurrentConflicts() => new(CurrentConflicts);

    public static IEnumerator ScanAllCoroutine()
    {
        if (_scanInProgress)
        {
            yield break;
        }

        _scanInProgress = true;
        try
        {
            yield return null;
            DetectSubnetsFromRuntime();
            yield return null;

            var servers = GregServerDiscoveryService.ScanAll();
            CurrentConflicts.Clear();
            CurrentConflicts.AddRange(ConflictDetector.DetectAll(servers, CurrentSubnets));

            GregEventBus.Publish(new ScanCompletedEvent
            {
                ServerCount = servers.Count,
                SwitchCount = UnityEngine.Object.FindObjectsOfType<NetworkSwitch>()?.Length ?? 0,
            });
        }
        finally
        {
            _scanInProgress = false;
        }
    }

    public static void ScanAll()
    {
        MelonCoroutines.Start(ScanAllCoroutine());
    }

    public static void DetectSubnetsFromRuntime()
    {
        CurrentSubnets.Clear();

        var cbs = GregCustomerService.GetAllCustomerBases();
        for (var i = 0; i < cbs.Count; i++)
        {
            var cb = cbs[i];
            if (cb == null)
            {
                continue;
            }

            var customerId = cb.customerID;
            var subnets = GregCustomerService.GetSubnetsPerApp(customerId);
            var vlans = GregCustomerService.GetVlanIdsPerApp(customerId);
            var pools = GregCustomerService.GetUsableIpsPerApp(customerId);
            var types = GregCustomerService.GetAppToServerType(customerId);

            foreach (var kv in subnets)
            {
                var appId = kv.Key;
                var cidr = kv.Value ?? "";
                var vlanId = vlans.TryGetValue(appId, out var v) ? v : 0;
                var reqType = types.TryGetValue(appId, out var t) ? t : 0;
                var allowed = pools.TryGetValue(appId, out var arr) ? arr?.ToList() ?? new List<string>() : new List<string>();

                ModLogging.Msg($"[CustomerService] Customer {customerId} App {appId}: cidr={cidr} vlan={vlanId} ips={allowed.Count}");

                CurrentSubnets.Add(new IpSubnet
                {
                    Id = $"c{customerId}-a{appId}",
                    Name = $"Customer-{customerId}-App-{appId}",
                    CustomerId = customerId,
                    AppId = appId,
                    Cidr = cidr,
                    VlanId = vlanId,
                    VlanEnabled = vlanId > 0,
                    RequiredServerType = reqType,
                    Type = SubnetType.CustomerApp,
                    Pool = new IpPool
                    {
                        SubnetId = $"c{customerId}-a{appId}",
                        AllowedIps = allowed,
                    },
                });
            }
        }
    }
}
