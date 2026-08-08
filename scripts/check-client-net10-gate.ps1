$ErrorActionPreference = "Stop"
$deadline = [DateTimeOffset]::Parse("2026-10-01T00:00:00Z")
if ([DateTimeOffset]::UtcNow -lt $deadline) {
    Write-Host "The .NET 10 client gate becomes blocking on $($deadline.ToString('u'))."
    exit 0
}

$projects = @(
    "CP6.Client.Api/CP6.Client.Api.csproj",
    "CP6.Client.Core/CP6.Client.Core.csproj",
    "CP6.Client.Tests/CP6.Client.Tests.csproj",
    "CP6.Desktop/CP6.Desktop.csproj",
    "CP6.Mobile/CP6.Mobile.csproj"
)
$stillNet8 = $projects | Where-Object {
    (Get-Content -LiteralPath $_ -Raw) -match "<TargetFramework>net8\.0"
}
if ($stillNet8.Count -gt 0) {
    throw "External client release blocked: .NET 10 LTS upgrade is overdue for: $($stillNet8 -join ', ')"
}
Write-Host ".NET 10 client target gate passed."
