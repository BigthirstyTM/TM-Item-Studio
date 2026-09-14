# Writer compatibility probe

Runs the same `Gbx.Parse<CGameItemModel>` / `Gbx.Save` operation used by the
Studio with an explicitly selected GBX.NET assembly. It accepts user-provided
paths and does not include item assets in the repository.

```powershell
dotnet run --project Tests\WriterCompatibility -- `
  -p:GbxAssemblyPath=C:\path\to\GBX.NET.dll -- `
  C:\path\source.Item.Gbx C:\path\output.Item.Gbx
```

To update the X coordinate of an existing pivot:

```powershell
dotnet run --project Tests\WriterCompatibility -- `
  -p:GbxAssemblyPath=C:\path\to\GBX.NET.dll -- `
  C:\path\source.Item.Gbx C:\path\output.Item.Gbx 0 0.1
```

The tool deliberately does not add pivots or repair missing rotations. It is a
writer A/B probe: test its output in Trackmania before treating a DLL as safe
for Studio exports.
