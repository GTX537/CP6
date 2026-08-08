# CP6 client OpenAPI generation

`Cp6ApiClient.cs` is the checked-in generated artifact for the native-client
surface. CI starts `CP6.WebApi`, downloads `/swagger/v1/swagger.json`, and runs:

```powershell
./scripts/check-openapi-client.ps1 -SwaggerUrl http://127.0.0.1:5080/swagger/v1/swagger.json
```

The check extracts the routes owned by `client-auth`, `client/bootstrap`,
device activation, v2 production tasks, and label jobs together with the
OpenAPI schema set. It recursively sorts every JSON object before hashing so
Windows PowerShell 5.1 and PowerShell 7 produce the same canonical surface.
It compares that hash with
`openapi/client-surface.sha256`. A contract change therefore cannot land
without intentionally regenerating/reviewing the typed client and updating
the recorded surface hash. The v1 route remains server-side for one release,
but native clients intentionally compile only against v2.
