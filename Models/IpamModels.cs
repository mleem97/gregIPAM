using System;
using System.Collections.Generic;

namespace DHCPSwitches.Models;

public enum SubnetType
{
    CustomerApp,
    Management,
    Transit,
}

public enum LeaseSource
{
    DhcpAuto,
    Manual,
    Reserved,
}

public enum ConflictType
{
    DuplicateIp,
    InvalidForCustomer,
    WrongApp,
    WrongServerType,
    NoIp,
    VlanNotIsolated,
    VlanOverBlocked,
}

public enum AssignMode
{
    Sequential,
    LowestFirst,
    Random,
}

[Serializable]
public sealed class IpPool
{
    public string SubnetId = "";
    public string RangeStart = "";
    public string RangeEnd = "";
    public List<string> AllowedIps = new();
    public List<string> UsedIps = new();

    public int TotalAddresses => AllowedIps?.Count ?? 0;

    public int UsedAddresses => UsedIps?.Count ?? 0;

    public float Utilization => TotalAddresses <= 0 ? 0f : (float)UsedAddresses / TotalAddresses;
}

[Serializable]
public sealed class IpSubnet
{
    public string Id = Guid.NewGuid().ToString("N");
    public string Cidr = "";
    public string Name = "";
    public int CustomerId;
    public int AppId;
    public int VlanId;
    public bool VlanEnabled;
    public int RequiredServerType;
    public IpPool Pool = new();
    public SubnetType Type = SubnetType.CustomerApp;
}

[Serializable]
public sealed class IpLease
{
    public string LeaseId = Guid.NewGuid().ToString("N");
    public string ServerId = "";
    public string Ip = "";
    public int CustomerId;
    public int AppId;
    public string SubnetId = "";
    public string AssignedAt = DateTime.UtcNow.ToString("o");
    public LeaseSource Source;
}

[Serializable]
public sealed class IpConflict
{
    public ConflictType Type;
    public string Ip = "";
    public List<string> AffectedServerIds = new();
    public string Suggestion = "";
    public bool AutoFixable;
}

[Serializable]
public sealed class DhcpReservation
{
    public string ReservationId = Guid.NewGuid().ToString("N");
    public string ServerId = "";
    public string Ip = "";
    public string SubnetId = "";
    public string Note = "";
}

[Serializable]
public sealed class VlanPolicyViolation
{
    public string SwitchId = "";
    public int PortIndex;
    public int ExpectedVlanId;
    public string ActualState = "";
    public ConflictType Type = ConflictType.VlanNotIsolated;
}

[Serializable]
public sealed class VlanApplyResult
{
    public int SwitchesAffected;
    public int PortsModified;
    public int FailedOperations;
    public string Summary = "";
}

public sealed class CliCommand
{
    public string Name = "";
    public string Syntax = "";
    public string Description = "";
    public string[] Aliases = Array.Empty<string>();
    public Func<string[], CliSession, string> Handler;
}

public sealed class CliSession
{
    public string ActiveSwitchId = "";
    public int ContextPortIndex = -1;
    public List<string> OutputHistory = new();
    public List<string> CommandHistory = new();
    public int HistoryIndex = -1;
    public string CurrentPrompt = "greg#";
    public bool AwaitingConfirm;
    public string PendingCommand = "";
}

public sealed class ServerInfo
{
    public string ServerId = "";
    public int CustomerId;
    public string Ip = "";
    public int ServerType;
    public bool IsOn;
    public bool IsBroken;
    public int RackPositionUID;
    public string RackId = "";
    public string SlotId = "";
    public NetworkServer Instance;
}
