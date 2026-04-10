# gregIPAM

Gamified **DHCP** and **IPAM** for **Data Center** (Unity/IL2CPP, MelonLoader). The shipped .NET assembly and root namespace remain **`DHCPSwitches`** (`DHCPSwitches.csproj`) for compatibility with existing installs and documentation.

---

## Part of gregFramework

This directory is part of the **[gregFramework](https://github.com/mleem97/gregFramework)** workspace. Clone sibling repositories side by side so each project lives at `gregFramework/<RepoName>/`. See the workspace [README](https://github.com/mleem97/gregFramework/blob/master/README.md) for the full layout and migration notes.

**Remote:** [`mleem97/DataCenter_DHCPSwitches`](https://github.com/mleem97/DataCenter_DHCPSwitches) — on-disk path: `gregFramework/gregIPAM/`.

---

## Overview

Focus: better IP/DHCP gameplay, VLAN and scope concepts, patch-port labeling, roadmap toward IPAM — see **`ROADMAP.md`**.

---

## Tech stack

| | |
|:---|:---|
| **Game** | Data Center |
| **Loader** | MelonLoader (`0.7.2+` target) |
| **Interop** | Il2CppInterop |
| **Language** | C# / .NET 6 |
| **Patching** | Harmony |

---

## Repository structure

- **`docs/SOURCE_LAYOUT.md`** — folder overview
- **`StreamingAssets.Mods/DataCenter_Router/`** — passive shop `config.json` template (see docs)
- **`Core/`** — MelonLoader entry (`Main.cs`, …)
- **`Networking/`**, **`Ipam/`**, **`Cli/`**, **`Config/`**, … — details in `docs/SOURCE_LAYOUT.md`

---

## Development

### Prerequisites

- Windows + **Data Center** (Steam)
- .NET SDK 6.x
- MelonLoader for the target build

### Clone

```bash
git clone https://github.com/mleem97/DataCenter_DHCPSwitches.git gregIPAM
cd gregIPAM
```

You may already have a local copy under `gregFramework/gregIPAM/`.

### References

`DHCPSwitches.csproj` references MelonLoader/IL2CPP stubs. Set `DataCenterGameDir` in `Directory.Build.props` to your game directory, or:

`dotnet build -p:DataCenterGameDir="..."`

### Build

```bash
dotnet build
```

Deploy the DLL to `Mods/`.

---

## Roadmap

- [`ROADMAP.md`](./ROADMAP.md)

---

## Contributing / license

- [`CONTRIBUTING.md`](./CONTRIBUTING.md)
- [`CODE_OF_CONDUCT.md`](./CODE_OF_CONDUCT.md)
- [`SECURITY.md`](./SECURITY.md)
- License: **MIT** — [`LICENSE`](./LICENSE)
