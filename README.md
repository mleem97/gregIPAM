# DHCPSwitches

> **Gamified networking systems for _Data Center_ (Unity/IL2CPP, MelonLoader).**

---

## Overview

`DHCPSwitches` is a gameplay-focused mod project for **Data Center** that expands network management depth while keeping the experience practical and fun.

The project focuses on:

- Better in-game IP assignment UX
- DHCP scope management (`VLAN` / `Switch` / `Global`)
- VLAN-aware operations and management network concepts
- Patch-port labeling and topology clarity
- Shared server / multi-tenant gameplay concepts
- Progressive roadmap toward a semi-full gamified IPAM layer

---

## Current Status

- Roadmap-first planning is active.
- Main plan is documented in `ROADMAP.md`.
- Core mod code targets `net6.0` and MelonLoader + IL2CPP interop.

---

## Tech Stack

- **Game:** Data Center
- **Runtime:** MelonLoader (`0.7.2+` target)
- **Interop:** Il2CppInterop
- **Language:** C# / .NET 6
- **Patching:** Harmony

---

## Repository Structure

- `Main.cs` — Mod entry point and runtime hooks
- `DHCPManager.cs` — DHCP assignment flow and patches
- `IPAMOverlay.cs` — in-game monitoring/feedback overlay
- `ROADMAP.md` — phased implementation roadmap
- `.github/copilot-instructions.md` — project-specific guidance

---

## Getting Started (Development)

### 1) Requirements

- Windows + Steam version of **Data Center**
- .NET SDK 6.x
- Visual Studio 2022/2026 or compatible C# IDE
- MelonLoader installed for the target game

### 2) Clone

```bash
git clone https://github.com/mleem97/DataCenter_DHCPSwitches.git
cd DataCenter_DHCPSwitches
```

### 3) Configure Local References

`DHCPSwitches.csproj` references local game assemblies (MelonLoader and IL2CPP assemblies).  
Adjust `HintPath` entries to your local installation path if needed.

### 4) Build

```bash
dotnet build
```

### 5) Deploy

Copy the built mod DLL to your game `Mods` folder.

---

## Roadmap

Implementation planning is maintained in:

- [`ROADMAP.md`](./ROADMAP.md)

This includes release phases, epics, risks, testing strategy, and sprint-ready tasks.

---

## Contributing

Please read:

- [`CONTRIBUTING.md`](./CONTRIBUTING.md)
- [`CODE_OF_CONDUCT.md`](./CODE_OF_CONDUCT.md)
- [`SECURITY.md`](./SECURITY.md)

---

## License

This project is licensed under the **MIT License**.  
See [`LICENSE`](./LICENSE) for full text.
