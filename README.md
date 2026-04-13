# DHCPSwitches

> **Gamified networking systems for _Data Center_ (Unity/IL2CPP, MelonLoader).**

---

## Overview

`DHCPSwitches` is a gameplay-focused mod project for **Data Center** that expands network management depth while keeping the experience practical and fun.

The project focuses on:

- Better in-game IP assignment UX
- DHCP scope management
- Shared server / multi-tenant gameplay concepts
- Progressive roadmap toward a gamified IPAM layer

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

- **`docs/SOURCE_LAYOUT.md`** — folder-by-folder map of all C# sources
- **`Core/`** — MelonLoader entry (`Main.cs`, `MelonModInfo.cs`, …)
- **`Networking/`** — DHCP, subnets, device helpers
- **`Ipam/`** — IPAM overlay (`IPAMOverlay.cs`), `LicenseManager`
- **`Config/`**, **`Patches/`**, **`Input/`**, **`Diagnostics/`** — see `docs/SOURCE_LAYOUT.md`
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

`DHCPSwitches.csproj` references **MelonLoader** (`MelonLoader\net6`) and IL2CPP stubs (`MelonLoader\Il2CppAssemblies`, with fallback to `BepInEx\interop`).  
Point `DataCenterGameDir` in `Directory.Build.props` at your game folder (or override with `dotnet build -p:DataCenterGameDir="..."`).

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

## Maintainers

- **Mod Author Team:** `teamGreg - mleem97 & mochimus`

---

## License

This project is licensed under the **MIT License**.  
See [`LICENSE`](./LICENSE) for full text.
