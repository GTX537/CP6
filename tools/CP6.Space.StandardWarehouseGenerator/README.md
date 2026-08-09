# CP6 Space standard warehouse generator

This tool deterministically generates the E07-S04 standard warehouse acceptance
package from `SpaceStandardWarehouseDatasetGenerator`.

```powershell
dotnet run `
  --project tools\CP6.Space.StandardWarehouseGenerator `
  -c Release -- `
  --output tmp\e07-s04\acceptance\v1.0.0
```

The command generates the DXF, JSON/JSONL, CSV, PNG, PDF, fault fixtures and
manifest. The fixed dataset version, timestamp, generator version and random
seed are declared in the application contract. Running it never uses an
unfixed random source. The output directory must be new or empty; this prevents
stale files from being silently incorporated into the manifest.

The package intentionally does not fabricate `warehouse-standard.dwg`.
E02-S01 records the licensed DWG converter decision as blocked; the manifest
keeps that gate visible until an approved converter and source artifact exist.
