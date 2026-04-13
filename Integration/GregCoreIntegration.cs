namespace DHCPSwitches;

/// <summary>
/// Placeholder bridge: in this workspace, gregCore services are provided by local compatibility shims
/// under <c>greg.Sdk.Services</c>. If the real gregCore repo is added, this adapter can switch bindings.
/// </summary>
public static class GregCoreIntegration
{
    public static string IntegrationMode => "LocalCompatibilityShim";
}
