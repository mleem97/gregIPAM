using System.Collections.Generic;

namespace greg.Sdk.Events;

public sealed class ServerOnlineEvent
{
    public string ServerId { get; set; } = "";
    public int CustomerId { get; set; }
    public string Ip { get; set; } = "";
}

public sealed class ServerOfflineEvent
{
    public string ServerId { get; set; } = "";
}

public sealed class IpChangedEvent
{
    public string ServerId { get; set; } = "";
    public string OldIp { get; set; } = "";
    public string NewIp { get; set; } = "";
}

public sealed class IpConflictEvent
{
    public string Ip { get; set; } = "";
    public List<string> AffectedServerIds { get; set; } = new();
}

public sealed class VlanPolicyChangedEvent
{
    public string SwitchId { get; set; } = "";
    public int PortIndex { get; set; }
    public int VlanId { get; set; }
}

public sealed class ScanCompletedEvent
{
    public int ServerCount { get; set; }
    public int SwitchCount { get; set; }
}
