# CP6 Space CAD Start controlled acceptance

This executable closes the WP2 CAD Start result gate without placing raw CAD
in Git. It verifies two frozen authorized samples (one DWG and one DXF), the
exact AutoCAD Primary Worker release and executable, then runs the product
`SpaceCadPreparationService` and `SpaceCadParseService` against a disposable
SQL Server database.

The run passes only when both formats produce a sealed Preparation, the Draft
stays unchanged during Preview, Parse Start replays idempotently, and a
tampered mapping hash is rejected without creating another job. The temporary
database is deleted in `finally`; the Worker independently removes every raw
CAD attempt directory.

The SQL Server connection is read only from `CP6_TEST_SQLSERVER`. Other inputs
are explicit command arguments:

```powershell
$env:CP6_TEST_SQLSERVER = '<disposable SQL Server connection>'
dotnet run --project tools/CP6.Space.CadStartAcceptance -c Release -- `
  --dataset-root D:\CP6-Controlled-CAD\space-golden-cad\v1.0.0-final `
  --release-root D:\CP6-Cad-Releases\space-autocad-worker\1.0.0-d2d0a0d1 `
  --accoreconsole 'D:\AutoCAD 2025\accoreconsole.exe' `
  --work-root D:\CP6-Cad-Work\wp2-cad-start `
  --application-commit <full-lowercase-git-sha> `
  --output D:\CP6-Cad-Evidence\wp2-controlled-execution.json
```

The output is controlled acceptance evidence. It does not claim production
data, production WMS, a production deployment, remote mTLS, or public SaaS use.
