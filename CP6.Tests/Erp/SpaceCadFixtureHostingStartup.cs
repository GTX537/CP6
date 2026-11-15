using System.Security.Cryptography;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Infrastructure;
using CP6.Tests.Erp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

[assembly: HostingStartup(typeof(SpaceCadFixtureHostingStartup))]

// The local API fixture loads this assembly before ASP.NET Core discovers hosting startups.
internal static class StartupHook
{
    public static void Initialize() { }
}

namespace CP6.Tests.Erp
{
    public sealed class SpaceCadFixtureHostingStartup : IHostingStartup
    {
        public void Configure(IWebHostBuilder builder) => builder.ConfigureServices(services =>
            services.AddScoped<ISpaceCadPreparationProvider, FixturePreparationProvider>());

        private sealed class FixturePreparationProvider(SpaceContext context) : ISpaceCadPreparationProvider
        {
            public async Task<SpaceCadIrPackageV1> InspectAsync(SpaceCadPreparationProviderRequest request, Stream source,
                CancellationToken cancellationToken = default)
            {
                if (context.Database.CurrentTransaction is not null)
                    throw new InvalidOperationException("CAD fixture preparation must run outside a database transaction.");
                var sourceHash = Convert.ToHexString(await SHA256.HashDataAsync(source, cancellationToken)).ToLowerInvariant();
                if (sourceHash != request.SourceSha256)
                    throw new InvalidDataException("The CAD fixture source hash differs.");

                var bounds = new SpaceCadBoundsV1(0, 0, 10_000, 0);
                return new SpaceCadIrPackageV1(
                    new SpaceCadIrDocumentV1(SpaceCadIrVersions.SchemaVersion, sourceHash, SpaceCadSourceFormat.Dxf, "AC1032",
                        SpaceCadUnit.Millimeter, 1, SpaceCadIrVersions.CoordinateSystem, bounds, "w605-local-cad", "1.0"),
                    [new SpaceCadIrLayerV1("WALL", "WALL", 1, "ACI:7", "CONTINUOUS")],
                    [],
                    [new SpaceCadIrEntityV1("H:WALL-1", SpaceCadIrEntityType.Line, "LINE", "WALL", null,
                        [new(0, 0), new(10_000, 0)], null, null, null, SpaceCadAffineTransformV1.Identity, bounds,
                        false, true, new Dictionary<string, string>())],
                    [],
                    new SpaceCadIrSummaryV1(LayerCount: 1, BlockCount: 0, EntityCount: 1, SupportedEntityCount: 1,
                        UnsupportedEntityCount: 0, MissingSourceRefCount: 0, Bounds: bounds));
            }
        }
    }
}
