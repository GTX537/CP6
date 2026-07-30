# Aspose.CAD E02-S01 experiment adapter

This project is an isolated candidate adapter for Aspose.CAD 26.6.0. It is
intentionally excluded from `CP6.slnx`; normal CP6 builds and CI do not restore
the commercial SDK package.

Build it explicitly:

```powershell
dotnet restore tools\CP6.Space.CadExperiment.AsposeAdapter
dotnet build tools\CP6.Space.CadExperiment.AsposeAdapter -c Release --no-restore
```

The adapter implements the experiment
[`adapter-contract-v1`](../../docs/space/experiments/e02-s01/adapter-contract-v1.md).
Without a license it runs in Aspose evaluation mode. For an approved temporary
or purchased license, set `CP6_SPACE_ASPOSE_LICENSE_PATH` to the absolute
license-file path before starting the adapter. The path is read from the process
environment and is never written to the observation or runner arguments.

Do not add the license file to the repository.
