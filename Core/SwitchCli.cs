using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using DHCPSwitches.Models;
using greg.Sdk.Services;
using MelonLoader;
using UnityEngine;

namespace DHCPSwitches;

public static class SwitchCli
{
    private const string Divider = "────────────────────────────────────────────────────────";
    private const string ErrorNoActiveSwitch = "% Error: no active switch";
    private const string PendingFactoryReset = "factory-reset";
    private const string PendingVlanReset = "vlan-reset";
    private const int MinVlan = 1;
    private const int MaxVlan = 4094;

    private static bool _inConfigMode;
    private static int _interfacePortIndex = -1;

    public static NetworkSwitch ActiveSwitch { get; set; }

    public static List<string> History { get; } = new();

    public static int HistoryIdx { get; set; } = -1;

    public static CliSession Session { get; } = new();

    private static readonly List<CliCommand> Commands = new()
    {
        new CliCommand { Name = "help", Syntax = "help", Description = "Show command list", Aliases = new[] { "?" }, Handler = (_, s) => BuildHelp() },
        new CliCommand { Name = "clear", Syntax = "clear", Description = "Clear terminal", Handler = (_, s) => "__CLEAR__" },
        new CliCommand { Name = "history", Syntax = "history", Description = "Show command history", Handler = (_, s) => string.Join("\n", History) },
        new CliCommand { Name = "switch list", Syntax = "switch list", Description = "List switches", Handler = (_, s) => CmdSwitchList() },
        new CliCommand { Name = "switch select", Syntax = "switch select {id|index}", Description = "Select active switch", Handler = CmdSwitchSelect },
        new CliCommand { Name = "switch info", Syntax = "switch info", Description = "Show active switch info", Handler = (_, s) => CmdSwitchInfo() },
        new CliCommand { Name = "show interfaces", Syntax = "show interfaces", Description = "Show all ports", Handler = (_, s) => CmdShowInterfaces() },
        new CliCommand { Name = "show interface", Syntax = "show interface {portIndex}", Description = "Show one interface detail", Handler = CmdShowInterface },
        new CliCommand { Name = "show vlan", Syntax = "show vlan [id]", Description = "Show vlan overview", Handler = CmdShowVlan },
        new CliCommand { Name = "show ip", Syntax = "show ip", Description = "Show discovered IP endpoints", Handler = (_, s) => CmdShowIp() },
        new CliCommand { Name = "show neighbors", Syntax = "show neighbors", Description = "Show network neighbors", Handler = (_, s) => CmdShowNeighbors() },
        new CliCommand { Name = "show status", Syntax = "show status", Description = "Show compact switch status", Handler = (_, s) => CmdShowStatus() },
        new CliCommand { Name = "show flow", Syntax = "show flow", Description = "Show per-port throughput view", Handler = (_, s) => CmdShowFlow() },
        new CliCommand { Name = "show running-config", Syntax = "show running-config", Description = "Show switch running config", Handler = (_, s) => CmdShowRunningConfig() },
        new CliCommand { Name = "show ipam", Syntax = "show ipam", Description = "Show lease view", Handler = (_, s) => CmdShowIpam() },
        new CliCommand { Name = "show dhcp", Syntax = "show dhcp", Description = "Show DHCP runtime mode/status", Handler = (_, s) => CmdShowDhcp() },
        new CliCommand { Name = "dhcp pool show", Syntax = "dhcp pool show", Description = "Show subnet pool usage", Handler = (_, s) => CmdDhcpPoolShow() },
        new CliCommand { Name = "dhcp mode", Syntax = "dhcp mode {sequential|lowest|random}", Description = "Set DHCP assignment strategy", Handler = CmdDhcpMode },
        new CliCommand { Name = "dhcp assign", Syntax = "dhcp assign {serverId}", Description = "Assign DHCP to one server", Handler = CmdDhcpAssign },
        new CliCommand { Name = "dhcp reservation add", Syntax = "dhcp reservation add {serverId} {ip} [note]", Description = "Add DHCP reservation", Handler = CmdDhcpReservationAdd },
        new CliCommand { Name = "dhcp reservation show", Syntax = "dhcp reservation show [serverId]", Description = "Show DHCP reservation(s)", Handler = CmdDhcpReservationShow },
        new CliCommand { Name = "dhcp reservation remove", Syntax = "dhcp reservation remove {serverId}", Description = "Remove reservation", Handler = CmdDhcpReservationRemove },
        new CliCommand { Name = "ipam scan", Syntax = "ipam scan", Description = "Run IPAM scan in current scene", Handler = (_, s) => CmdIpamScan() },
        new CliCommand { Name = "ipam conflict check", Syntax = "ipam conflict check", Description = "List detected IP/VLAN conflicts", Handler = (_, s) => CmdIpamConflictCheck() },
        new CliCommand { Name = "configure terminal", Syntax = "configure terminal", Description = "Enter global config mode", Aliases = new[] { "conf t" }, Handler = (_, s) => CmdConfigureTerminal() },
        new CliCommand { Name = "interface", Syntax = "interface <portIndex>", Description = "Enter interface config mode", Handler = CmdInterface },
        new CliCommand { Name = "switchport vlan allow", Syntax = "switchport vlan allow {vlanId}", Description = "Allow VLAN on current port", Handler = CmdSwitchportVlanAllow },
        new CliCommand { Name = "switchport vlan block", Syntax = "switchport vlan block {vlanId}", Description = "Block VLAN on current port", Handler = CmdSwitchportVlanBlock },
        new CliCommand { Name = "switchport vlan clear", Syntax = "switchport vlan clear", Description = "Clear VLAN filters on current port", Handler = (_, s) => CmdSwitchportVlanClear() },
        new CliCommand { Name = "switchport vlan show", Syntax = "switchport vlan show", Description = "Show blocked VLANs on current port", Handler = (_, s) => CmdSwitchportVlanShow() },
        new CliCommand { Name = "no switchport vlan block", Syntax = "no switchport vlan block {vlanId}", Description = "Alias for allow VLAN on current port", Handler = CmdNoSwitchportVlanBlock },
        new CliCommand { Name = "switchport mode", Syntax = "switchport mode {access|trunk}", Description = "Set port mode", Handler = CmdSwitchportMode },
        new CliCommand { Name = "switchport access vlan", Syntax = "switchport access vlan <id>", Description = "Set access VLAN", Handler = CmdSwitchportAccessVlan },
        new CliCommand { Name = "switchport trunk native vlan", Syntax = "switchport trunk native vlan <id>", Description = "Set trunk native VLAN", Handler = CmdSwitchportTrunkNativeVlan },
        new CliCommand { Name = "switchport trunk allowed vlan", Syntax = "switchport trunk allowed vlan <list|all|none>", Description = "Set trunk allowed list", Handler = CmdSwitchportTrunkAllowedVlan },
        new CliCommand { Name = "vlan", Syntax = "vlan <id>", Description = "Create VLAN in switch database", Handler = CmdVlanCreate },
        new CliCommand { Name = "vlan reset", Syntax = "vlan reset", Description = "Clear all VLAN filters on active switch", Handler = (_, s) => CmdVlanReset() },
        new CliCommand { Name = "vlan policy check", Syntax = "vlan policy check", Description = "Check vlan policy violations", Handler = (_, s) => CmdVlanPolicyCheck() },
        new CliCommand { Name = "vlan policy apply", Syntax = "vlan policy apply {vlanId}", Description = "Apply vlan policy for one VLAN", Handler = CmdVlanPolicyApply },
        new CliCommand { Name = "write memory", Syntax = "write memory", Description = "Save running-config", Aliases = new[] { "copy running-config startup-config" }, Handler = (_, s) => CmdWriteMemory() },
        new CliCommand { Name = "end", Syntax = "end", Description = "Exit config mode", Handler = (_, s) => CmdEnd() },
        new CliCommand { Name = "exit", Syntax = "exit", Description = "Exit current mode", Handler = (_, s) => CmdExit() },
        new CliCommand { Name = "shutdown", Syntax = "shutdown", Description = "Power off active switch", Handler = (_, s) => CmdShutdown(true) },
        new CliCommand { Name = "no shutdown", Syntax = "no shutdown", Description = "Power on active switch", Handler = (_, s) => CmdShutdown(false) },
        new CliCommand { Name = "reload", Syntax = "reload", Description = "Reload active switch", Handler = (_, s) => CmdReload() },
        new CliCommand { Name = "factory-reset", Syntax = "factory-reset", Description = "Reset switch config", Handler = (_, s) => CmdFactoryReset() },
        new CliCommand { Name = "update-ui", Syntax = "update-ui", Description = "Refresh switch screen", Handler = (_, s) => CmdUpdateUi() },
    };

    public static string HostnameLine => BuildPrompt();

    public static string Execute(string rawInput)
    {
        var input = (rawInput ?? "").Trim();
        ModLogging.Msg($"[CLI] Execute: '{input}'");

        if (string.IsNullOrWhiteSpace(input))
        {
            return "";
        }

        if (Session.AwaitingConfirm)
        {
            var confirmed = string.Equals(input, "y", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(input, "yes", StringComparison.OrdinalIgnoreCase);
            var pending = Session.PendingCommand ?? "";
            Session.AwaitingConfirm = false;
            Session.PendingCommand = "";
            if (!confirmed)
            {
                return "% Info: cancelled";
            }

            return ExecutePendingConfirmed(pending);
        }

        History.Add(input);
        HistoryIdx = History.Count;

        if (TryHandleContextualCommand(input, out var contextual))
        {
            return contextual;
        }

        var lower = input.ToLowerInvariant();
        for (var i = 0; i < Commands.Count; i++)
        {
            var cmd = Commands[i];
            if (Matches(lower, cmd))
            {
                var args = Tokenize(input);
                var output = cmd.Handler(args, Session) ?? "";
                ModLogging.Msg($"[CLI] Command: {cmd.Name} | Args: {string.Join(",", args)}");
                return output;
            }
        }

        return $"% Error: unknown command '{input}'";
    }

    private static string ExecutePendingConfirmed(string pending)
    {
        if (string.Equals(pending, PendingFactoryReset, StringComparison.OrdinalIgnoreCase))
        {
            return CmdFactoryResetConfirmed();
        }

        if (string.Equals(pending, PendingVlanReset, StringComparison.OrdinalIgnoreCase))
        {
            return CmdVlanResetConfirmed();
        }

        return "% Warning: no pending action";
    }

    public static List<string> GetCompletions(string partial)
    {
        var p = (partial ?? "").Trim().ToLowerInvariant();
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < Commands.Count; i++)
        {
            var c = Commands[i];
            if (c.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(c.Name);
            }

            for (var a = 0; a < c.Aliases.Length; a++)
            {
                if (c.Aliases[a].StartsWith(p, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(c.Aliases[a]);
                }
            }
        }

        return result.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool Matches(string lowerInput, CliCommand cmd)
    {
        if (lowerInput == cmd.Name || lowerInput.StartsWith(cmd.Name + " ", StringComparison.Ordinal))
        {
            return true;
        }

        for (var i = 0; i < cmd.Aliases.Length; i++)
        {
            var alias = cmd.Aliases[i];
            if (lowerInput == alias || lowerInput.StartsWith(alias + " ", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string[] Tokenize(string input)
    {
        return input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string BuildHelp()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Available commands:");
        sb.AppendLine(Divider);
        for (var i = 0; i < Commands.Count; i++)
        {
            var c = Commands[i];
            sb.AppendLine($"{c.Syntax,-28} {c.Description}");
        }

        sb.AppendLine(Divider);
        sb.AppendLine("Tip: mode transitions follow Cisco style: exec -> conf -> interface");

        return sb.ToString().TrimEnd();
    }

    private static bool TryHandleContextualCommand(string input, out string output)
    {
        output = null;

        if (string.Equals(input, "exit", StringComparison.OrdinalIgnoreCase))
        {
            output = CmdExit();
            return true;
        }

        if (string.Equals(input, "end", StringComparison.OrdinalIgnoreCase))
        {
            output = CmdEnd();
            return true;
        }

        if (!_inConfigMode)
        {
            return false;
        }

        if (_interfacePortIndex >= 0)
        {
            if (input.StartsWith("switchport ", StringComparison.OrdinalIgnoreCase))
            {
                var args = Tokenize(input);
                if (args.Length >= 3
                    && string.Equals(args[1], "mode", StringComparison.OrdinalIgnoreCase))
                {
                    output = CmdSwitchportMode(args, Session);
                    return true;
                }

                if (args.Length >= 4
                    && string.Equals(args[1], "access", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(args[2], "vlan", StringComparison.OrdinalIgnoreCase))
                {
                    output = CmdSwitchportAccessVlan(args, Session);
                    return true;
                }

                if (args.Length >= 6
                    && string.Equals(args[1], "trunk", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(args[2], "native", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(args[3], "vlan", StringComparison.OrdinalIgnoreCase))
                {
                    output = CmdSwitchportTrunkNativeVlan(args, Session);
                    return true;
                }

                if (args.Length >= 6
                    && string.Equals(args[1], "trunk", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(args[2], "allowed", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(args[3], "vlan", StringComparison.OrdinalIgnoreCase))
                {
                    output = CmdSwitchportTrunkAllowedVlan(args, Session);
                    return true;
                }
            }
        }

        if (input.StartsWith("interface ", StringComparison.OrdinalIgnoreCase))
        {
            output = CmdInterface(Tokenize(input), Session);
            return true;
        }

        if (input.StartsWith("vlan ", StringComparison.OrdinalIgnoreCase))
        {
            output = CmdVlanCreate(Tokenize(input), Session);
            return true;
        }

        return false;
    }

    private static string CmdSwitchList()
    {
        var arr = UnityEngine.Object.FindObjectsOfType<NetworkSwitch>();
        if (arr == null || arr.Length == 0)
        {
            return "% Warning: no switches found";
        }

        var sb = new StringBuilder();
        sb.AppendLine("Idx  SwitchId                     Status");
        sb.AppendLine(Divider);
        for (var i = 0; i < arr.Length; i++)
        {
            var sw = arr[i];
            if (sw == null)
            {
                continue;
            }

            var state = sw.isOn ? "UP" : "DOWN";
            sb.AppendLine($"{i,-4} {sw.switchId,-28} {state}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string CmdSwitchSelect(string[] args, CliSession session)
    {
        if (args.Length < 3)
        {
            return "% Error: syntax: switch select {id|index}";
        }

        var all = UnityEngine.Object.FindObjectsOfType<NetworkSwitch>();
        if (all == null || all.Length == 0)
        {
            return "% Warning: no switches found";
        }

        var key = args[2];
        NetworkSwitch pick = null;
        if (int.TryParse(key, out var idx) && idx >= 0 && idx < all.Length)
        {
            pick = all[idx];
        }
        else
        {
            pick = all.FirstOrDefault(sw => sw != null && string.Equals(sw.switchId, key, StringComparison.OrdinalIgnoreCase));
        }

        if (pick == null)
        {
            return $"% Error: switch not found '{key}'";
        }

        ActiveSwitch = pick;
        Session.ActiveSwitchId = pick.switchId ?? "";
        Session.CurrentPrompt = HostnameLine;
        ModLogging.Msg($"[CLI] Active switch: {Session.ActiveSwitchId}");
        return $"Active switch set to {Session.ActiveSwitchId}";
    }

    private static string CmdSwitchInfo()
    {
        if (ActiveSwitch == null)
        {
            return "% Error: no active switch (use 'switch select ...')";
        }

        var ports = ActiveSwitch.cableLinkSwitchPorts?.Count ?? 0;
        return string.Join(
            "\n",
            $"SwitchId: {ActiveSwitch.switchId}",
            $"Label:    {ActiveSwitch.label}",
            $"Status:   {(ActiveSwitch.isOn ? "UP" : "DOWN")}",
            $"Broken:   {ActiveSwitch.isBroken}",
            $"Ports:    {ports}");
    }

    private static string CmdShowInterfaces()
    {
        if (ActiveSwitch == null)
        {
            return ErrorNoActiveSwitch;
        }

        var links = ActiveSwitch.cableLinkSwitchPorts;
        var count = links?.Count ?? 0;
        var sb = new StringBuilder();
        sb.AppendLine("Port  Connected  Speed     Blocked VLANs");
        sb.AppendLine(Divider);
        for (var i = 0; i < count; i++)
        {
            var l = links[i];
            var connected = l != null && l.connected;
            var speed = l != null ? l.connectionSpeed : 0f;
            var blocked = string.Join(",", GregVlanService.GetDisallowedVlans(ActiveSwitch, i));
            if (string.IsNullOrWhiteSpace(blocked))
            {
                blocked = "none";
            }

            sb.AppendLine($"{i,-5} {(connected ? "YES" : "NO"),-10} {speed,5:0.0}G   {blocked}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string CmdShowInterface(string[] args, CliSession session)
    {
        if (ActiveSwitch == null)
        {
            return ErrorNoActiveSwitch;
        }

        if (args.Length < 3 || !int.TryParse(args[2], out var portIndex))
        {
            return "% Error: syntax: show interface {portIndex}";
        }

        var links = ActiveSwitch.cableLinkSwitchPorts;
        var count = links?.Count ?? 0;
        if (portIndex < 0 || portIndex >= count)
        {
            return $"% Error: invalid port index {portIndex}";
        }

        var link = links[portIndex];
        var blocked = GregVlanService.GetDisallowedVlans(ActiveSwitch, portIndex);
        var blockedText = blocked.Count == 0 ? "none" : string.Join(",", blocked);
        return string.Join(
            "\n",
            $"Port Index:       {portIndex}",
            $"Connected:        {(link != null && link.connected)}",
            $"Speed:            {(link != null ? link.connectionSpeed : 0f):0.0} Gbps",
            $"Disallowed VLANs: {blockedText}",
            $"Cable ID:         {(link != null ? link.id.ToString() : "n/a")}");
    }

    private static string CmdShowVlan(string[] args, CliSession session)
    {
        if (ActiveSwitch == null)
        {
            return ErrorNoActiveSwitch;
        }

        var filters = GregVlanService.GetAllPortFilters(ActiveSwitch);
        if (args.Length >= 3 && int.TryParse(args[2], out var vlanId))
        {
            var allow = GregVlanService.GetPortsAllowingVlan(ActiveSwitch, vlanId);
            var block = GregVlanService.GetPortsBlockingVlan(ActiveSwitch, vlanId);
            return string.Join(
                "\n",
                $"VLAN {vlanId}",
                $"Allowed ports: {string.Join(",", allow)}",
                $"Blocked ports: {string.Join(",", block)}");
        }

        var sb = new StringBuilder();
        sb.AppendLine("Port  Blocked VLANs");
        sb.AppendLine(Divider);
        foreach (var kv in filters)
        {
            var blocked = kv.Value.Count == 0 ? "none" : string.Join(",", kv.Value);
            sb.AppendLine($"{kv.Key,-5} {blocked}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string CmdShowIp()
    {
        var endpoints = ServerScanAdapter.GetSwitchEndpoints(ActiveSwitch);
        if (endpoints.Count == 0)
        {
            return "% Warning: no servers discovered";
        }

        var ordered = endpoints
            .Where(e => e != null && !string.IsNullOrWhiteSpace(e.Ip))
            .OrderBy(e => e.PortIndex < 0 ? int.MaxValue : e.PortIndex)
            .ThenBy(e => e.ServerId ?? "", StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ordered.Count == 0)
        {
            return "% Warning: no servers discovered";
        }

        var sb = new StringBuilder();
        sb.AppendLine("Port   Switch          Server               IP               Customer App VLAN Source");
        sb.AppendLine(Divider);
        for (var i = 0; i < ordered.Count; i++)
        {
            var e = ordered[i];

            var port = e.PortIndex >= 0 ? e.PortIndex.ToString() : "n/a";
            var sw = string.IsNullOrWhiteSpace(e.SwitchId) ? "-" : e.SwitchId;
            sb.AppendLine($"{port,-6} {sw,-15} {e.ServerId,-20} {e.Ip,-16} {e.CustomerId,8} {e.AppId,3} {e.VlanId,4} {e.Source}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string CmdShowNeighbors()
    {
        if (ActiveSwitch == null)
        {
            return ErrorNoActiveSwitch;
        }

        var endpoints = ServerScanAdapter.GetSwitchEndpoints(ActiveSwitch);
        var ordered = endpoints
            .Where(e => e != null && !string.IsNullOrWhiteSpace(e.Ip))
            .OrderBy(e => e.PortIndex < 0 ? int.MaxValue : e.PortIndex)
            .ThenBy(e => e.ServerId ?? "", StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ordered.Count == 0)
        {
            return "% Warning: no neighbors";
        }

        var links = ActiveSwitch.cableLinkSwitchPorts;
        var sb = new StringBuilder();
        sb.AppendLine("Port   Neighbor Type  Target               IP               Link  VLAN Source");
        sb.AppendLine(Divider);
        for (var i = 0; i < ordered.Count; i++)
        {
            var e = ordered[i];
            var portText = e.PortIndex >= 0 ? e.PortIndex.ToString() : "n/a";
            var linkState = "?";
            if (e.PortIndex >= 0 && e.PortIndex < (links?.Count ?? 0))
            {
                var link = links[e.PortIndex];
                linkState = link != null && link.connected ? "UP" : "DOWN";
            }

            sb.AppendLine($"{portText,-6} {"server",-14} {e.ServerId,-20} {e.Ip,-16} {linkState,-5} {e.VlanId,4} {e.Source}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string CmdShowStatus()
    {
        if (ActiveSwitch == null)
        {
            return ErrorNoActiveSwitch;
        }

        var ports = ActiveSwitch.cableLinkSwitchPorts?.Count ?? 0;
        var flow = DHCPManager.IsFlowPaused ? "Paused" : "Running";
        return $"{ActiveSwitch.switchId} | {(ActiveSwitch.isOn ? "UP" : "DOWN")} | ports={ports} | Flow={flow} | DHCPMode={DHCPManager.DhcpAssignMode}";
    }

    private static string CmdShowFlow()
    {
        if (ActiveSwitch == null)
        {
            return ErrorNoActiveSwitch;
        }

        var links = ActiveSwitch.cableLinkSwitchPorts;
        var count = links?.Count ?? 0;
        var total = 0f;
        var connected = 0;

        var sb = new StringBuilder();
        sb.AppendLine($"Flow Status: {(DHCPManager.IsFlowPaused ? "Paused" : "Active")}");
        sb.AppendLine("Per-Port Speed:");
        for (var i = 0; i < count; i++)
        {
            var l = links[i];
            var speed = l != null ? l.connectionSpeed : 0f;
            var isConn = l != null && l.connected;
            if (isConn)
            {
                connected++;
                total += speed;
            }

            sb.AppendLine($"  Port {i}: {speed:0.0} Gbps ({(isConn ? "UP" : "DOWN")})");
        }

        sb.AppendLine($"Total Speed: {total:0.0} Gbps");
        sb.AppendLine($"Connected Ports: {connected}");
        return sb.ToString().TrimEnd();
    }

    private static string CmdShowDhcp()
    {
        var flow = DHCPManager.IsFlowPaused ? "Paused" : "Running";
        return $"DHCP flow={flow} empty-autofill={DHCPManager.EmptyIpAutoFillEnabled} assign-mode={DHCPManager.DhcpAssignMode}";
    }

    private static string CmdShowRunningConfig()
    {
        if (!TryGetSwitchConfig(out var cfg, out var portCount, out var error))
        {
            return error;
        }

        var sb = new StringBuilder();
        sb.AppendLine("!");
        sb.AppendLine($"hostname {(ActiveSwitch?.switchId ?? "switch")}");
        sb.AppendLine("!");
        for (var i = 0; i < portCount; i++)
        {
            var p = i < cfg.Ports.Count ? cfg.Ports[i] : null;
            var blocked = BuildBlockedVlanListFromConfigPort(p);
            sb.AppendLine($"interface port{i}");
            sb.AppendLine($" switchport mode {(p?.Mode ?? "access")}");
            sb.AppendLine($" switchport access vlan {(p?.AccessVlan ?? 1)}");
            sb.AppendLine($" switchport trunk native vlan {(p?.NativeVlan ?? 1)}");
            sb.AppendLine($" switchport vlan blocked {(blocked.Length == 0 ? "none" : blocked)}");
        }

        sb.AppendLine("!");
        sb.AppendLine("end");
        return sb.ToString().TrimEnd();
    }

    private static string CmdShowIpam()
    {
        var leases = LeaseStore.GetAllLeases();
        if (leases.Count == 0)
        {
            return "% Warning: no leases";
        }

        var sb = new StringBuilder();
        sb.AppendLine("IP               Server               Customer App Source");
        sb.AppendLine(Divider);
        for (var i = 0; i < leases.Count; i++)
        {
            var lease = leases[i];
            if (lease == null)
            {
                continue;
            }

            sb.AppendLine($"{lease.Ip,-16} {lease.ServerId,-20} {lease.CustomerId,8} {lease.AppId,3} {lease.Source}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string CmdDhcpPoolShow()
    {
        var subnets = IpamEngine.GetCurrentSubnets();
        if (subnets.Count == 0)
        {
            IpamEngine.DetectSubnetsFromRuntime();
            subnets = IpamEngine.GetCurrentSubnets();
        }

        if (subnets.Count == 0)
        {
            return "% Warning: no subnets discovered";
        }

        var leaseBySubnet = LeaseStore.GetAllLeases()
            .Where(x => x != null)
            .GroupBy(x => x.SubnetId ?? "")
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine("Subnet             Total  Used  Free  Util%");
        sb.AppendLine(Divider);
        for (var i = 0; i < subnets.Count; i++)
        {
            var s = subnets[i];
            if (s == null)
            {
                continue;
            }

            var total = s.Pool?.AllowedIps?.Count ?? 0;
            var used = leaseBySubnet.TryGetValue(s.Id ?? "", out var u) ? u : 0;
            var free = Math.Max(0, total - used);
            var util = total <= 0 ? 0f : (float)used / total * 100f;
            sb.AppendLine($"{s.Cidr,-18} {total,5} {used,5} {free,5} {util,5:0.0}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string CmdDhcpMode(string[] args, CliSession session)
    {
        if (args.Length < 3)
        {
            return "% Error: syntax: dhcp mode {sequential|lowest|random}";
        }

        var token = args[2].Trim().ToLowerInvariant();
        AssignMode mode;
        switch (token)
        {
            case "sequential":
                mode = AssignMode.Sequential;
                break;
            case "lowest":
            case "lowestfirst":
            case "lowest-first":
                mode = AssignMode.LowestFirst;
                break;
            case "random":
                mode = AssignMode.Random;
                break;
            default:
                return $"% Error: unsupported dhcp mode '{token}'";
        }

        DHCPManager.SetAssignMode(mode);
        return $"DHCP assign mode set to {mode}";
    }

    private static string CmdDhcpAssign(string[] args, CliSession session)
    {
        if (args.Length < 3)
        {
            return "% Error: syntax: dhcp assign {serverId}";
        }

        var serverId = args[2];
        var servers = UnityEngine.Object.FindObjectsOfType<Server>();
        var server = servers?.FirstOrDefault(s => s != null && string.Equals(s.serverID, serverId, StringComparison.OrdinalIgnoreCase));
        if (server == null)
        {
            return $"% Error: server not found '{serverId}'";
        }

        var ok = DHCPManager.AssignDhcpToSingleServer(server);
        return ok ? $"Assigning IP to {serverId}... done" : $"% Error: DHCP assign failed for {serverId}";
    }

    private static string CmdDhcpReservationAdd(string[] args, CliSession session)
    {
        if (args.Length < 5)
        {
            return "% Error: syntax: dhcp reservation add {serverId} {ip} [note]";
        }

        var serverId = args[3];
        var ip = args[4];
        if (!GregIpService.IsValidIp(ip))
        {
            return $"% Error: invalid IP '{ip}'";
        }

        var note = args.Length > 5 ? string.Join(" ", args.Skip(5)) : "";
        LeaseStore.AddReservation(new DhcpReservation
        {
            ReservationId = Guid.NewGuid().ToString("N"),
            ServerId = serverId,
            Ip = ip,
            SubnetId = "",
            Note = note,
        });

        return $"Reservation added: {serverId} -> {ip}";
    }

    private static string CmdDhcpReservationShow(string[] args, CliSession session)
    {
        if (args.Length >= 4)
        {
            var one = LeaseStore.GetReservation(args[3]);
            if (one == null)
            {
                return $"% Warning: no reservation for {args[3]}";
            }

            return $"{one.ServerId} -> {one.Ip}" + (string.IsNullOrWhiteSpace(one.Note) ? "" : $" ({one.Note})");
        }

        var all = LeaseStore.GetAllReservations();
        if (all.Count == 0)
        {
            return "% Warning: no reservations";
        }

        var sb = new StringBuilder();
        sb.AppendLine("Server               Reserved IP       Note");
        sb.AppendLine(Divider);
        for (var i = 0; i < all.Count; i++)
        {
            var r = all[i];
            if (r == null)
            {
                continue;
            }

            sb.AppendLine($"{r.ServerId,-20} {r.Ip,-16} {r.Note}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string CmdDhcpReservationRemove(string[] args, CliSession session)
    {
        if (args.Length < 4)
        {
            return "% Error: syntax: dhcp reservation remove {serverId}";
        }

        var removed = LeaseStore.RemoveReservation(args[3]);
        return removed ? $"Reservation removed for {args[3]}" : $"% Warning: no reservation found for {args[3]}";
    }

    private static string CmdIpamScan()
    {
        IpamEngine.DetectSubnetsFromRuntime();
        var subnets = IpamEngine.GetCurrentSubnets();
        var servers = GregServerDiscoveryService.ScanAll();
        var conflicts = ConflictDetector.DetectAll(servers, subnets);
        return $"Scanning... done. {servers.Count} servers, {conflicts.Count} conflicts";
    }

    private static string CmdIpamConflictCheck()
    {
        var conflicts = IpamEngine.GetCurrentConflicts();
        if (conflicts.Count == 0)
        {
            var servers = GregServerDiscoveryService.ScanAll();
            var subnets = IpamEngine.GetCurrentSubnets();
            conflicts = ConflictDetector.DetectAll(servers, subnets);
        }

        if (conflicts.Count == 0)
        {
            return "✓ No conflicts detected";
        }

        var sb = new StringBuilder();
        sb.AppendLine("Type                IP               Affected");
        sb.AppendLine(Divider);
        for (var i = 0; i < conflicts.Count; i++)
        {
            var c = conflicts[i];
            if (c == null)
            {
                continue;
            }

            sb.AppendLine($"{c.Type,-18} {(c.Ip ?? ""),-16} {(c.AffectedServerIds?.Count ?? 0)}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string CmdVlanPolicyCheck()
    {
        var violations = VlanPolicyEngine.AnalyzeAll(IpamEngine.GetCurrentSubnets())
            .Where(v => ActiveSwitch == null || string.Equals(v.SwitchId, ActiveSwitch.switchId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (violations.Count == 0)
        {
            return "✓ All policies compliant";
        }

        var sb = new StringBuilder();
        sb.AppendLine("Switch               Port  Expected VLAN  State");
        sb.AppendLine(Divider);
        for (var i = 0; i < violations.Count; i++)
        {
            var v = violations[i];
            sb.AppendLine($"{v.SwitchId,-20} {v.PortIndex,4} {v.ExpectedVlanId,13}  {v.ActualState}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string CmdVlanPolicyApply(string[] args, CliSession session)
    {
        if (args.Length < 4 || !int.TryParse(args[3], out var vlanId) || vlanId <= 0)
        {
            return "% Error: syntax: vlan policy apply {vlanId}";
        }

        var subnets = IpamEngine.GetCurrentSubnets();
        if (subnets.Count == 0)
        {
            IpamEngine.DetectSubnetsFromRuntime();
            subnets = IpamEngine.GetCurrentSubnets();
        }

        var subnet = subnets.FirstOrDefault(s => s != null && s.VlanId == vlanId);
        if (subnet == null)
        {
            return $"% Error: no subnet found for vlan {vlanId}";
        }

        var res = VlanPolicyEngine.ApplySubnetVlanPolicy(subnet);
        return res?.Summary ?? "% Error: vlan policy apply failed";
    }

    private static string CmdShutdown(bool down)
    {
        if (ActiveSwitch == null)
        {
            return ErrorNoActiveSwitch;
        }

        try
        {
            if (down)
            {
                ActiveSwitch.TurnOffCommonFunctions();
                ActiveSwitch.isOn = false;
                return $"Switch {ActiveSwitch.switchId} going down...";
            }

            ActiveSwitch.isOn = true;
            ActiveSwitch.TurnOnCommonFunction();
            ActiveSwitch.UpdateScreenUI();
            return $"Bringing {ActiveSwitch.switchId} up...";
        }
        catch (Exception ex)
        {
            return "% Error: " + ex.Message;
        }
    }

    private static string CmdUpdateUi()
    {
        if (ActiveSwitch == null)
        {
            return ErrorNoActiveSwitch;
        }

        ActiveSwitch.UpdateScreenUI();
        return "Screen UI refreshed.";
    }

    private static string CmdReload()
    {
        if (ActiveSwitch == null)
        {
            return ErrorNoActiveSwitch;
        }

        MelonCoroutines.Start(ReloadSwitchCoroutine(ActiveSwitch));
        return $"Reloading switch {ActiveSwitch.switchId}...";
    }

    private static string CmdVlanReset()
    {
        if (ActiveSwitch == null)
        {
            return ErrorNoActiveSwitch;
        }

        Session.AwaitingConfirm = true;
        Session.PendingCommand = PendingVlanReset;
        return "This clears ALL VLAN filters. Confirm? [y/N]";
    }

    private static string CmdVlanResetConfirmed()
    {
        if (ActiveSwitch == null)
        {
            return ErrorNoActiveSwitch;
        }

        var changed = GregVlanService.ClearAllFilters(ActiveSwitch);
        return $"VLAN filters cleared on {changed} port(s).";
    }

    private static IEnumerator ReloadSwitchCoroutine(NetworkSwitch sw)
    {
        if (sw == null)
        {
            yield break;
        }

        try
        {
            sw.TurnOffCommonFunctions();
            sw.isOn = false;
        }
        catch
        {
            // ignored
        }

        yield return new WaitForSeconds(1f);

        try
        {
            sw.isOn = true;
            sw.TurnOnCommonFunction();
            sw.UpdateScreenUI();
        }
        catch
        {
            // ignored
        }
    }

    private static string CmdFactoryReset()
    {
        if (ActiveSwitch == null)
        {
            return ErrorNoActiveSwitch;
        }

        Session.AwaitingConfirm = true;
        Session.PendingCommand = PendingFactoryReset;
        return "This will wipe all config. Confirm? [y/N]";
    }

    private static string CmdFactoryResetConfirmed()
    {
        if (ActiveSwitch == null)
        {
            return ErrorNoActiveSwitch;
        }

        try
        {
            var ports = ActiveSwitch.cableLinkSwitchPorts?.Count ?? 0;
            GregVlanService.ClearAllFilters(ActiveSwitch);
            DeviceConfigRegistry.EraseSwitchConfig(ActiveSwitch, Math.Max(ports, 0));
            ActiveSwitch.UpdateScreenUI();
            return "Factory reset complete.\n" + CmdShowStatus();
        }
        catch (Exception ex)
        {
            return "% Error: factory-reset failed: " + ex.Message;
        }
    }

    private static string CmdConfigureTerminal()
    {
        if (ActiveSwitch == null)
        {
            return ErrorNoActiveSwitch;
        }

        _inConfigMode = true;
        _interfacePortIndex = -1;
        Session.CurrentPrompt = HostnameLine;
        return "Enter configuration commands, one per line. End with CNTL/Z.";
    }

    private static string CmdInterface(string[] args, CliSession session)
    {
        if (!_inConfigMode)
        {
            return "% Error: 'interface' only valid in config mode (use 'configure terminal')";
        }

        if (!TryGetSwitchConfig(out _, out var portCount, out var err))
        {
            return err;
        }

        if (args.Length < 2)
        {
            return "% Error: syntax: interface <portIndex>";
        }

        if (!TryParsePortIndex(args[1], portCount, out var portIndex))
        {
            return $"% Error: invalid interface '{args[1]}'";
        }

        _interfacePortIndex = portIndex;
        Session.ContextPortIndex = portIndex;
        Session.CurrentPrompt = HostnameLine;
        return $"Configuring interface {portIndex}";
    }

    private static string CmdSwitchportMode(string[] args, CliSession session)
    {
        if (!TryGetInterfaceConfig(out var cfg, out var port, out var err))
        {
            return err;
        }

        if (args.Length < 3)
        {
            return "% Error: syntax: switchport mode {access|trunk}";
        }

        var mode = args[2].Trim().ToLowerInvariant();
        if (mode != "access" && mode != "trunk")
        {
            return $"% Error: unsupported mode '{mode}'";
        }

        port.Mode = mode;
        port.Trunk = mode == "trunk";
        if (string.IsNullOrWhiteSpace(port.AllowedVlanRaw))
        {
            port.AllowedVlanRaw = port.Trunk ? "all" : port.AccessVlan.ToString();
        }

        cfg.Ports[_interfacePortIndex] = port;
        return $"Port {_interfacePortIndex} mode set to {mode}";
    }

    private static string CmdSwitchportVlanAllow(string[] args, CliSession session)
    {
        if (!TryGetInterfaceConfig(out _, out _, out var err))
        {
            return err;
        }

        if (args.Length < 4 || !TryParseVlan(args[3], out var vlanId))
        {
            return "% Error: syntax: switchport vlan allow {vlanId}";
        }

        var ok = GregVlanService.AllowVlan(ActiveSwitch, _interfacePortIndex, vlanId);
        return ok ? $"VLAN {vlanId} allowed on port {_interfacePortIndex}" : "% Error: failed to allow vlan";
    }

    private static string CmdSwitchportVlanBlock(string[] args, CliSession session)
    {
        if (!TryGetInterfaceConfig(out _, out _, out var err))
        {
            return err;
        }

        if (args.Length < 4 || !TryParseVlan(args[3], out var vlanId))
        {
            return "% Error: syntax: switchport vlan block {vlanId}";
        }

        var ok = GregVlanService.BlockVlan(ActiveSwitch, _interfacePortIndex, vlanId);
        return ok ? $"VLAN {vlanId} blocked on port {_interfacePortIndex}" : "% Error: failed to block vlan";
    }

    private static string CmdNoSwitchportVlanBlock(string[] args, CliSession session)
    {
        if (args.Length < 5)
        {
            return "% Error: syntax: no switchport vlan block {vlanId}";
        }

        var mapped = new[] { "switchport", "vlan", "allow", args[4] };
        return CmdSwitchportVlanAllow(mapped, Session);
    }

    private static string CmdSwitchportVlanClear()
    {
        if (!TryGetInterfaceConfig(out _, out _, out var err))
        {
            return err;
        }

        var ok = GregVlanService.ClearPortFilters(ActiveSwitch, _interfacePortIndex);
        return ok ? $"Port {_interfacePortIndex} vlan filters cleared" : "% Error: failed to clear filters";
    }

    private static string CmdSwitchportVlanShow()
    {
        if (!TryGetInterfaceConfig(out _, out _, out var err))
        {
            return err;
        }

        var blocked = GregVlanService.GetDisallowedVlans(ActiveSwitch, _interfacePortIndex);
        return blocked.Count == 0
            ? $"Port {_interfacePortIndex} blocked VLANs: none"
            : $"Port {_interfacePortIndex} blocked VLANs: {string.Join(",", blocked)}";
    }

    private static string CmdSwitchportAccessVlan(string[] args, CliSession session)
    {
        if (!TryGetInterfaceConfig(out var cfg, out var port, out var err))
        {
            return err;
        }

        if (args.Length < 4 || !TryParseVlan(args[3], out var vlanId))
        {
            return "% Error: syntax: switchport access vlan <1-4094>";
        }

        EnsureVlanExists(cfg, vlanId);
        port.AccessVlan = vlanId;
        if (!port.Trunk)
        {
            port.AllowedVlanRaw = vlanId.ToString();
        }

        cfg.Ports[_interfacePortIndex] = port;
        return $"Port {_interfacePortIndex} access vlan set to {vlanId}";
    }

    private static string CmdSwitchportTrunkNativeVlan(string[] args, CliSession session)
    {
        if (!TryGetInterfaceConfig(out var cfg, out var port, out var err))
        {
            return err;
        }

        if (args.Length < 6 || !TryParseVlan(args[5], out var vlanId))
        {
            return "% Error: syntax: switchport trunk native vlan <1-4094>";
        }

        EnsureVlanExists(cfg, vlanId);
        port.NativeVlan = vlanId;
        cfg.Ports[_interfacePortIndex] = port;
        return $"Port {_interfacePortIndex} native vlan set to {vlanId}";
    }

    private static string CmdSwitchportTrunkAllowedVlan(string[] args, CliSession session)
    {
        if (!TryGetInterfaceConfig(out var cfg, out var port, out var err))
        {
            return err;
        }

        if (args.Length < 6)
        {
            return "% Error: syntax: switchport trunk allowed vlan <list|all|none>";
        }

        var raw = string.Join("", args.Skip(5)).Trim();
        if (!ValidateAllowedVlanRaw(raw, out var validateErr))
        {
            return validateErr;
        }

        var ids = ExpandVlanRaw(raw);
        for (var i = 0; i < ids.Count; i++)
        {
            EnsureVlanExists(cfg, ids[i]);
        }

        port.AllowedVlanRaw = raw.ToLowerInvariant();
        cfg.Ports[_interfacePortIndex] = port;
        return $"Port {_interfacePortIndex} trunk allowed list set to '{port.AllowedVlanRaw}'";
    }

    private static string CmdVlanCreate(string[] args, CliSession session)
    {
        if (!_inConfigMode)
        {
            return "% Error: 'vlan' only valid in config mode";
        }

        if (!TryGetSwitchConfig(out var cfg, out _, out var err))
        {
            return err;
        }

        if (args.Length < 2 || !TryParseVlan(args[1], out var vlanId))
        {
            return "% Error: syntax: vlan <1-4094>";
        }

        if (cfg.Vlans.Any(v => v != null && v.Id == vlanId))
        {
            return $"VLAN {vlanId} already exists";
        }

        cfg.Vlans.Add(new SwitchVlanEntry { Id = vlanId, Name = $"vlan{vlanId}" });
        cfg.Vlans = cfg.Vlans.Where(v => v != null).OrderBy(v => v.Id).ToList();
        return $"VLAN {vlanId} created";
    }

    private static string CmdWriteMemory()
    {
        var ok = DeviceConfigRegistry.TrySaveAllToDisk();
        return ok ? "[OK] Configuration saved." : "% Error: failed to save configuration";
    }

    private static string CmdEnd()
    {
        _inConfigMode = false;
        _interfacePortIndex = -1;
        Session.ContextPortIndex = -1;
        Session.CurrentPrompt = HostnameLine;
        return "";
    }

    private static string CmdExit()
    {
        if (_interfacePortIndex >= 0)
        {
            _interfacePortIndex = -1;
            Session.ContextPortIndex = -1;
            Session.CurrentPrompt = HostnameLine;
            return "";
        }

        if (_inConfigMode)
        {
            _inConfigMode = false;
            Session.CurrentPrompt = HostnameLine;
            return "";
        }

        return "% Info: already in exec mode";
    }

    private static bool TryGetSwitchConfig(out SwitchRuntimeConfig cfg, out int portCount, out string error)
    {
        cfg = null;
        portCount = 0;
        error = null;

        if (ActiveSwitch == null)
        {
            error = ErrorNoActiveSwitch;
            return false;
        }

        portCount = ActiveSwitch.cableLinkSwitchPorts?.Count ?? 0;
        if (portCount <= 0)
        {
            error = "% Error: active switch has no ports";
            return false;
        }

        cfg = DeviceConfigRegistry.GetOrCreateSwitch(ActiveSwitch, portCount);
        if (cfg == null)
        {
            error = "% Error: failed to access running-config";
            return false;
        }

        return true;
    }

    private static bool TryGetInterfaceConfig(out SwitchRuntimeConfig cfg, out SwitchPortConfig port, out string error)
    {
        cfg = null;
        port = null;
        error = null;

        if (!_inConfigMode || _interfacePortIndex < 0)
        {
            error = "% Error: command requires interface config mode";
            return false;
        }

        if (!TryGetSwitchConfig(out cfg, out _, out error))
        {
            return false;
        }

        if (_interfacePortIndex >= cfg.Ports.Count)
        {
            error = "% Error: interface index out of range";
            return false;
        }

        port = cfg.Ports[_interfacePortIndex] ?? new SwitchPortConfig { PortIndex = _interfacePortIndex };
        return true;
    }

    private static bool TryParsePortIndex(string token, int portCount, out int portIndex)
    {
        portIndex = -1;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var t = token.Trim();
        if (int.TryParse(t, out var direct))
        {
            if (direct >= 0 && direct < portCount)
            {
                portIndex = direct;
                return true;
            }

            if (direct >= 1 && direct <= portCount)
            {
                portIndex = direct - 1;
                return true;
            }

            return false;
        }

        var slash = t.LastIndexOf('/');
        if (slash >= 0 && slash < t.Length - 1 && int.TryParse(t[(slash + 1)..], out var suffix))
        {
            if (suffix >= 0 && suffix < portCount)
            {
                portIndex = suffix;
                return true;
            }

            if (suffix >= 1 && suffix <= portCount)
            {
                portIndex = suffix - 1;
                return true;
            }
        }

        return false;
    }

    private static bool TryParseVlan(string token, out int vlanId)
    {
        vlanId = 0;
        return int.TryParse(token, out vlanId) && vlanId >= MinVlan && vlanId <= MaxVlan;
    }

    private static string BuildBlockedVlanListFromConfigPort(SwitchPortConfig port)
    {
        if (port == null)
        {
            return string.Empty;
        }

        var list = new List<int>();
        for (var vlan = MinVlan; vlan <= MaxVlan; vlan++)
        {
            if (!VlanRuntimeSync.IsVlanAllowedOnPort(port, vlan))
            {
                list.Add(vlan);
            }

            if (list.Count >= 64)
            {
                break;
            }
        }

        return string.Join(",", list);
    }

    private static string BuildPrompt()
    {
        var host = ActiveSwitch != null ? (ActiveSwitch.switchId ?? "switch") : "switch";
        if (_interfacePortIndex >= 0)
        {
            return $"{host}(config-if)#";
        }

        if (_inConfigMode)
        {
            return $"{host}(config)#";
        }

        return $"{host}#";
    }

    private static void EnsureVlanExists(SwitchRuntimeConfig cfg, int vlanId)
    {
        if (cfg.Vlans.Any(v => v != null && v.Id == vlanId))
        {
            return;
        }

        cfg.Vlans.Add(new SwitchVlanEntry { Id = vlanId, Name = $"vlan{vlanId}" });
        cfg.Vlans = cfg.Vlans.Where(v => v != null).OrderBy(v => v.Id).ToList();
    }

    private static bool ValidateAllowedVlanRaw(string raw, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "% Error: allowed vlan list cannot be empty";
            return false;
        }

        var text = raw.Trim().ToLowerInvariant();
        if (text == "all" || text == "none")
        {
            return true;
        }

        var items = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (items.Length == 0)
        {
            error = "% Error: invalid vlan list";
            return false;
        }

        for (var i = 0; i < items.Length; i++)
        {
            var token = items[i];
            if (token.Contains('-'))
            {
                var parts = token.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length != 2 || !TryParseVlan(parts[0], out _) || !TryParseVlan(parts[1], out _))
                {
                    error = $"% Error: invalid vlan range '{token}'";
                    return false;
                }

                continue;
            }

            if (!TryParseVlan(token, out _))
            {
                error = $"% Error: invalid vlan id '{token}'";
                return false;
            }
        }

        return true;
    }

    private static List<int> ExpandVlanRaw(string raw)
    {
        var result = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return result.ToList();
        }

        var text = raw.Trim().ToLowerInvariant();
        if (text == "all" || text == "none")
        {
            return result.ToList();
        }

        var items = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < items.Length; i++)
        {
            var token = items[i];
            if (token.Contains('-'))
            {
                var parts = token.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length != 2 || !TryParseVlan(parts[0], out var a) || !TryParseVlan(parts[1], out var b))
                {
                    continue;
                }

                if (a > b)
                {
                    (a, b) = (b, a);
                }

                for (var v = a; v <= b; v++)
                {
                    result.Add(v);
                }

                continue;
            }

            if (TryParseVlan(token, out var one))
            {
                result.Add(one);
            }
        }

        return result.OrderBy(v => v).ToList();
    }
}
