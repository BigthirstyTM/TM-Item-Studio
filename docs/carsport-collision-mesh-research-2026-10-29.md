# Carsport collision route and mesh replacement research (2026-10-29)

## Scope and safety

This note inspects copies and existing user test files only. It does not modify
Trackmania installation files, `Trackmania.exe`, native game assets, or the
source map. The generated ref-swap fixtures are disposable files under the
user `Documents/Trackmania/Items/TMIS_CarSport_RefSwap` folder.

## What the working Carsport item actually contains

`Documents/Trackmania/Items/CarSport_COLLIDABLE.Item.Gbx` parses and round-trips
as:

```text
root:       CGameItemModel
ident:      (CarSport, Common, Nadeo)
ItemType:   Ornament
ItemTypeE:  Ornament
entity:     CGameVehicleModel
```

The file is 6,596 bytes and has SHA-256
`2547ED9A39FEE0B58C562B52219D93C26614DE3743F69CC5460BC283E76D02DE`.
The `CGameVehicleModel` has no inline `CPlugSolid2Model`, `CPlugSurface`, or
`CPlugDynaObjectModel`. Its only model data is:

```text
PhyModelFile = Vehicles\\Cars\\CarSport\\PhyModelSport.VehiclePhyModel.Gbx
VisModelFile = Vehicles\\Cars\\CarSport\\VisModelSport.VehicleVisModel.Gbx
ItemOccupantSlotModels = []
```

The stock extracted `Vehicles/Items/Cars/CarSport.Item.Gbx` is 6,585 bytes,
SHA-256 `883608EA40EBAACD8E20596F5454E515A47BB43F185291B552AE7A238C6033C8`.
It has the same external references and the same `CGameVehicleModel` graph, but
its `ItemType`/`ItemTypeE` is `Vehicle`. The collidable copy's extra bytes are
metadata: its description is `CarSport_COLLIDABLE`; they are not a replacement
mesh or a new collision surface.

The four stock vehicle physics/visual pairs available in the extracted game
assets are:

```text
CarDesert  DesertCar.VehiclePhyModel.Gbx / VisModelDesert.VehicleVisModel.Gbx
CarRally   CarRally.VehiclePhyModel.Gbx / VisModelRally.VehicleVisModel.Gbx
CarSnow    PhyModelSnow.VehiclePhyModel.Gbx / VisModelSnow.VehicleVisModel.Gbx
CarSport   PhyModelSport.VehiclePhyModel.Gbx / VisModelSport.VehicleVisModel.Gbx
```

These are vehicle-specific native classes (`CPlugVehiclePhyModel` and
`CPlugVehicleVisModel`), not the ordinary item mesh/collision classes.

## Why the earlier mesh replacement failed

The Item Studio preview/import path explicitly classifies `CGameVehicleModel` as
`previewOnly` (see `Pages/Home.razor`). The ordinary mesh editor expects a
`CPlugSolid2Model`/visual triangle graph, while Carsport's visual geometry is
behind `VisModelFile`; its collision is behind `PhyModelFile`. Replacing an
ordinary item mesh therefore cannot change this vehicle body.

Changing only the item metadata is enough to make the stock vehicle appear as a
placeable Ornament. `COLLIDABLECARS.Map.Gbx` confirms the map is a standard
`TrackMania\\TM_Race` map with one external anchor at `<16,8,16>`; there is no
alternate game mode or script requirement in the map header.

## Controlled reference-swap probe

On copies of `CarSport_COLLIDABLE.Item.Gbx`, only `CGameVehicleModel.PhyModelFile`
and `.VisModelFile` were changed to the stock CarSnow, CarRally, and CarDesert
pairs. The outputs are:

```text
Items/TMIS_CarSport_RefSwap/CarSport_COLLIDABLE_SnowRefs.Item.Gbx
  6,596 B  SHA-256 577480BFBE9486EA9ED09A3605564F036B43F19062170C6C2F51D60005AC7317
Items/TMIS_CarSport_RefSwap/CarSport_COLLIDABLE_RallyRefs.Item.Gbx
  6,595 B  SHA-256 56219EBF66238EC290EF453ED496AA448A7D1C5B7EEBBE35EC8A54C7196DEDA5
Items/TMIS_CarSport_RefSwap/CarSport_COLLIDABLE_DesertRefs.Item.Gbx
  6,599 B  SHA-256 EDDC1F3663EC0AA1B89AB6D9EE6F9F87A1403120A19F298ECB6B03A6BC8AC032
```

All three parse and round-trip successfully. This proves the external vehicle
reference pair is a real authoring seam, but it is only a switch between stock
vehicle implementations; it is not a custom mesh route.

## Consequences for a custom ball

1. The Carsport trick is useful as evidence that Trackmania can instantiate a
   placeable object with native vehicle collision behavior in a normal race map.
2. It does not provide a generic mesh-replacement slot. A custom visual would
   require authoring a compatible `CPlugVehicleVisModel`; custom collision would
   require a compatible `CPlugVehiclePhyModel`. Those formats are not currently
   decoded or authored by TM Item Studio, and ordinary imported item meshes
   cannot be assigned to them.
3. Keeping the Carsport vehicle model while hiding/replacing its visual is not a
   safe native solution: the special vehicle physics and visual model are a
   coupled pair, and no verified field exists for substituting a generic
   `CPlugSolid2Model`.
4. The practical native choices remain: (a) retain Carsport as a stock-vehicle
   collision probe; (b) author a true `CPlugDynaObjectModel` item once the
   destructible scene managers are available; or (c) use a static/kinematic
   custom mesh as a visual approximation. The latter two are separate routes,
   not modifications to the Carsport item.

## Verification commands

```powershell
dotnet run --project Tests/KinematicReference -- --dump `
  'C:\Users\PC\Documents\Trackmania\Items\CarSport_COLLIDABLE.Item.Gbx'

dotnet run --project Tests/MapInspect -- `
  'C:\Users\PC\Documents\Trackmania\Maps\My Maps\GBX_ScriptingMaps\COLLIDABLECARS.Map.Gbx'
```
