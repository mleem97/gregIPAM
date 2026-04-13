using System;
using MelonLoader;
using UnityEngine;
using greg.Sdk.Services;
using greg.Sdk.Events;

[assembly: MelonInfo(typeof(gregIPAM.Mod), "gregIPAM", "0.4.0", "Mleem97")]
[assembly: MelonGame("Waseku", "Data Center")]

namespace gregIPAM
{
    public class Mod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            MelonLogger.Msg("Initializing gregIPAM v0.4.0...");
            
            // Register mod in GregCore
            GregModRegistry.Register("gregIPAM", "0.4.0");

            // Subscribe to ServerOnlineEvent to handle DHCP Auto-Assign
            GregEventBus.Subscribe<ServerOnlineEvent>("gregIPAM", OnServerOnline);
            
            MelonLogger.Msg("gregIPAM initialization complete.");
        }

        private void OnServerOnline(ServerOnlineEvent evt)
        {
            MelonLogger.Msg($"[DHCP] Server {evt.ServerId} online. Initiating auto-assign...");
            
            var serverInfo = GregServerDiscoveryService.GetById(evt.ServerId);
            if (serverInfo == null) return;
            
            // Basic logic demonstration
            if (GregIpService.HasIp(serverInfo.Instance))
            {
                MelonLogger.Msg($"[DHCP] Server {evt.ServerId} already has IP: {evt.Ip}");
                return;
            }

            // In a real implementation we would fetch app id, subnet pool, and assign.
            // This is just the core integration shell.
            string newIp = "192.168.0.100"; // Mock
            GregIpService.SetIp(serverInfo.Instance, newIp);
            MelonLogger.Msg($"[DHCP] Assigned {newIp} to {evt.ServerId}");
        }

        public override void OnUpdate()
        {
            // Toggle F9 Panel
            // if (UnityEngine.Input.GetKeyDown(KeyCode.F9))
            // {
            //     ToggleIpamPanel();
            // }
        }

        private void ToggleIpamPanel()
        {
            MelonLogger.Msg("[IPAM] Toggling F9 Panel...");
            // GregUiService.CreatePanel(...) logic would go here
        }
    }
}