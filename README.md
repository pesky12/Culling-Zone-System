<div align="center">

# Camera Culling Zone

*Dynamic camera culling system for VRChat worlds with zone-based distance overrides and platform-specific settings.*

[![VPM](https://img.shields.io/badge/VPM-8B5CF6?style=flat-square&logo=unity&logoColor=white)](https://pesky12.github.io/PeskyBox/index.json)
[![License](https://img.shields.io/badge/License-MIT-EC4899?style=flat-square)](LICENSE)

</div>

---

A flexible camera culling solution that lets you control layer visibility distances dynamically. Perfect for optimizing VRChat worlds by reducing draw distance for specific layers, with support for zone-based overrides and separate desktop/mobile configurations.

## Features

- **CameraCullingManager** — Central manager that applies culling distances to the player's cameras with desktop/mobile platform support
- **CameraCullingZone** — Trigger zones that override ambient culling settings when the player enters them
- **Platform-Specific Settings** — Separate culling configurations for desktop and mobile (Quest) platforms
- **Clip Plane Override** — Optionally override near/far clip planes per platform
- **Priority-Based Zones** — Higher priority zones override lower priority zones when overlapping

## Installation

### Via VPM (Recommended)

```
https://pesky12.github.io/PeskyBox/index.json
```

Copy the URL above and add it to your [VRChat Creator Companion](https://vcc.docs.vrchat.com/) package listings.

### Manual

1. Download the latest release from the [GitHub releases page](https://github.com/pesky12/com.pesky.box.cullingzone/releases)
2. Extract the `com.pesky.box.cullingzone` folder into your Unity project's `Assets/Packages` directory
3. Open Unity and let it compile
4. Have a snack!

## Quick Start

1. Add the `CameraCullingManager` prefab to your scene
2. Configure layer culling distances in the inspector (separate arrays for desktop and mobile)
3. (Optional) Add `CameraCullingZone` prefabs to create areas with different culling settings
4. (Optional) Enable clip plane overrides for additional performance tuning

## Components

| Component | Description |
|-----------|-------------|
| **CameraCullingManager** | Main manager that applies culling distances to screen and photo cameras. Supports platform-specific settings and dynamic zone switching. |
| **CameraCullingZone** | Trigger zone that overrides the manager's ambient settings when the player enters. Supports priority-based zone stacking. |
| **CameraCullingPlatformSettings** | Serializable class for platform-specific culling configuration. |

## Dependencies

- VRChat Worlds SDK 3.5.x (includes UdonSharp)

## License

MIT License — Free to use in any project, commercial or otherwise.

**Restriction:** Redistribution or resale as a standalone asset package is prohibited. You may include this in your own projects, games, and worlds, but you may not sell or redistribute it as a standalone Unity package or asset store product.

---

<div align="center">

Made with 💜 by [Pesky12](https://github.com/pesky12)

</div>