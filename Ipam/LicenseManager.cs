using UnityEngine.InputSystem;

namespace DHCPSwitches;

/// <summary>
/// Feature gates for DHCP / IPAM. Default: enabled. Ctrl+D toggles (for testing locked state).
/// Later: hook into ComputerShop / save unlock GUIDs (see DHCPSwitchesMod constants).
/// </summary>
internal static class LicenseManager
{
    private static bool _simulateLocked;

    internal static bool IsDHCPUnlocked => !_simulateLocked;
    internal static bool IsIPAMUnlocked => !_simulateLocked;

    internal static void HandleDebugUnlock()
    {
        var kb = Keyboard.current;
        if (kb == null)
        {
            return;
        }

        if (!kb.leftCtrlKey.isPressed || !kb.dKey.wasPressedThisFrame)
        {
            return;
        }

        _simulateLocked = !_simulateLocked;
        ModLogging.Msg(_simulateLocked
            ? "Debug: DHCP/IPAM locked (Ctrl+D again to unlock)."
            : "Debug: DHCP/IPAM unlocked.");
    }
}
