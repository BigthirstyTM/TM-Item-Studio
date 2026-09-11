# Trackmania 2020 Item Studio

Trackmania 2020 Item Studio is a browser-based editor for `.Item.Gbx` files.
It provides an interactive 3D viewport and exposes item metadata, placement,
kinematics, lights, sockets, physics and visual information.

## Features

- Load one or more Trackmania item files.
- Detect and browse embedded multi-variant items.
- Switch variants in the 3D viewport.
- Inspect and edit placement pivots, sockets, lights and kinematic settings.
- Export edited items, including multi-variant items.
- Export visible geometry as OBJ.

## Requirements

- .NET 8 SDK with the Blazor WebAssembly workload.
- A modern browser with WebAssembly support.

## Run locally

```bash
dotnet restore
dotnet run
```

The development server uses the URL configured in `Properties/launchSettings.json`.

## Notes

The project includes a compatibility-patched `GBX.NET` assembly. Editor++
multi-variant items can contain valid `0xFFFFFFFF` null node references inside
surface data. The upstream parser currently treats one of these references as
an unknown class ID, so the local assembly preserves the official API and
handles that sentinel as a null reference.