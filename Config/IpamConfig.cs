using DHCPSwitches.Models;

namespace DHCPSwitches;

public sealed class IpamConfig
{
    public AssignMode AssignMode = AssignMode.LowestFirst;
    public bool AutoAssignOnServerOnline = true;
    public float PollIntervalSeconds = 2f;
}
