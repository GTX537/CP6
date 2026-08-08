using System.Text.Json;
using CP6.Space.Application;

namespace CP6.Space.UnitTests;

public sealed class SpaceAiGenerationProviderImplementationsTests
{
    public static TheoryData<IWarehouseGenerationProvider> Providers =>
        new()
        {
            new MockWarehouseGenerationProvider(),
            new LocalHeuristicWarehouseGenerationProvider(),
        };

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Mock_and_local_providers_share_a_bounded_deterministic_contract(
        IWarehouseGenerationProvider provider)
    {
        var input = Input();

        var first = await provider.GenerateAsync(input, default);
        var repeat = await provider.GenerateAsync(input, default);

        AssertResult(input, first);
        Assert.Equal(
            JsonSerializer.Serialize(first),
            JsonSerializer.Serialize(repeat));
        Assert.DoesNotContain(
            "tenantId",
            JsonSerializer.Serialize(first),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Local_provider_uses_only_allowlisted_categories_and_reports_no_match()
    {
        var input = Input();
        var result = await new LocalHeuristicWarehouseGenerationProvider()
            .GenerateAsync(input, default);

        Assert.Collection(
            result.Suggestions.OrderBy(item => item.SourceKey),
            item =>
            {
                Assert.Equal("source-rack", item.SourceKey);
                Assert.Equal(WarehouseSpaceType.Rack, item.SuggestedType);
                Assert.Equal(WarehouseRackType.Unknown, item.Attributes.RackType);
            },
            item =>
            {
                Assert.Equal("source-zone", item.SourceKey);
                Assert.Equal(WarehouseSpaceType.Zone, item.SuggestedType);
                Assert.Equal(
                    WarehouseZonePurpose.Storage,
                    item.Attributes.ZonePurpose);
            });
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("LOCAL_HEURISTIC_NO_MATCH", diagnostic.Code);
        Assert.Equal("source-generic", diagnostic.SourceKey);
    }

    [Fact]
    public async Task Mock_provider_emits_one_predictable_suggestion_per_capped_feature()
    {
        var input = Input(maxSuggestions: 2);

        var result = await new MockWarehouseGenerationProvider()
            .GenerateAsync(input, default);

        Assert.Equal(2, result.Suggestions.Count);
        Assert.All(result.Suggestions, item => Assert.Equal(0.5m, item.Confidence));
        Assert.Equal("MOCK_PROVIDER_ACTIVE", Assert.Single(result.Diagnostics).Code);
    }

    [Theory]
    [InlineData(
        WarehouseGenerationProviderFailureKind.Unavailable,
        "AI_PROVIDER_UNAVAILABLE_FALLBACK")]
    [InlineData(
        WarehouseGenerationProviderFailureKind.Timeout,
        "AI_PROVIDER_TIMEOUT_FALLBACK")]
    [InlineData(
        WarehouseGenerationProviderFailureKind.RateLimited,
        "AI_PROVIDER_RATE_LIMITED_FALLBACK")]
    public async Task Retryable_primary_failures_degrade_to_the_same_provider_contract(
        WarehouseGenerationProviderFailureKind failureKind,
        string diagnosticCode)
    {
        var fallback = new CountingProvider(new LocalHeuristicWarehouseGenerationProvider());
        IWarehouseGenerationProvider provider = new FallbackWarehouseGenerationProvider(
            new FailingProvider(failureKind),
            fallback);

        var result = await provider.GenerateAsync(Input(), default);

        Assert.Equal("cp6-local-heuristic-v1", result.ProviderModel);
        Assert.Equal(1, fallback.CallCount);
        Assert.Contains(result.Diagnostics, item => item.Code == diagnosticCode
            && item.Severity == WarehouseDiagnosticSeverity.Warning);
        AssertResult(Input(), result);
    }

    [Fact]
    public async Task Contract_failure_and_cancellation_never_silently_fallback()
    {
        var fallback = new CountingProvider(new MockWarehouseGenerationProvider());
        var invalidPrimary = new FallbackWarehouseGenerationProvider(
            new FailingProvider(
                WarehouseGenerationProviderFailureKind.ContractViolation),
            fallback);

        var exception = await Assert.ThrowsAsync<
            WarehouseGenerationProviderException>(() =>
            invalidPrimary.GenerateAsync(Input(), default));
        Assert.Equal(
            WarehouseGenerationProviderFailureKind.ContractViolation,
            exception.FailureKind);
        Assert.False(exception.CanFallback);
        Assert.Equal(0, fallback.CallCount);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = new FallbackWarehouseGenerationProvider(
            new CancellingProvider(),
            fallback);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cancelled.GenerateAsync(Input(), cancellation.Token));
        Assert.Equal(0, fallback.CallCount);
    }

    [Fact]
    public void Provider_failure_exception_exposes_only_stable_safe_details()
    {
        var exception = new WarehouseGenerationProviderException(
            WarehouseGenerationProviderFailureKind.Timeout);

        Assert.Equal("AI_PROVIDER_TIMEOUT", exception.StableCode);
        Assert.True(exception.CanFallback);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain("http", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("key", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertResult(
        WarehouseGenerationInput input,
        WarehouseGenerationResult result)
    {
        Assert.Equal(WarehouseGenerationInput.CurrentSchemaVersion, result.SchemaVersion);
        Assert.Matches("^[a-z]+-[0-9a-f]{32}$", result.ProviderRequestId);
        Assert.InRange(result.ProviderModel.Length, 1, 128);
        Assert.InRange(result.Suggestions.Count, 0, input.Limits.MaxSuggestions);
        Assert.InRange(result.Diagnostics.Count, 0, 1_000);
        var sourceKeys = input.Features
            .Select(item => item.SourceKey)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(
            result.Suggestions.Count,
            result.Suggestions.Select(item => item.SourceKey).Distinct().Count());
        Assert.All(result.Suggestions, item =>
        {
            Assert.Contains(item.SourceKey, sourceKeys);
            Assert.True(Enum.IsDefined(item.SuggestedType));
            Assert.InRange(item.Confidence, 0, 1);
            Assert.InRange(
                item.Relations.Count,
                0,
                input.Limits.MaxRelationsPerSuggestion);
            Assert.NotEmpty(item.EvidenceCodes);
            Assert.All(item.Relations, relation =>
            {
                Assert.Contains(relation.TargetSourceKey, sourceKeys);
                Assert.InRange(relation.Confidence, 0, 1);
            });
        });
        Assert.True(result.Usage.InputUnits >= 0);
        Assert.True(result.Usage.OutputUnits >= 0);
    }

    private static WarehouseGenerationInput Input(int maxSuggestions = 10) =>
        new(
            new string('a', 64),
            SpaceAiDataPolicy.StructuredFeatures,
            new WarehouseGenerationLimits(maxSuggestions, 4),
            [
                Feature(
                    "source-rack",
                    WarehouseCadEntityType.BlockReference,
                    "layer-generic-111111111111111111111111",
                    "block-rack-222222222222222222222222",
                    ["source-zone"]),
                Feature(
                    "source-zone",
                    WarehouseCadEntityType.ClosedPolyline,
                    "layer-storage-zone-333333333333333333333333",
                    null,
                    ["source-rack"]),
                Feature(
                    "source-generic",
                    WarehouseCadEntityType.Line,
                    "layer-generic-444444444444444444444444",
                    null,
                    []),
            ],
            [],
            []);

    private static WarehouseGenerationFeature Feature(
        string sourceKey,
        WarehouseCadEntityType entityType,
        string layerToken,
        string? blockToken,
        IReadOnlyList<string> relations) =>
        new(
            sourceKey,
            entityType,
            layerToken,
            blockToken,
            1,
            new WarehouseNormalizedBounds(0, 0, 0.5m, 0.5m),
            0,
            null,
            [],
            relations,
            0);

    private sealed class FailingProvider(
        WarehouseGenerationProviderFailureKind failureKind) :
        IWarehouseGenerationProvider
    {
        public Task<WarehouseGenerationResult> GenerateAsync(
            WarehouseGenerationInput input,
            CancellationToken cancellationToken) =>
            throw new WarehouseGenerationProviderException(failureKind);
    }

    private sealed class CancellingProvider : IWarehouseGenerationProvider
    {
        public Task<WarehouseGenerationResult> GenerateAsync(
            WarehouseGenerationInput input,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Cancellation was not observed.");
        }
    }

    private sealed class CountingProvider(IWarehouseGenerationProvider inner) :
        IWarehouseGenerationProvider
    {
        public int CallCount { get; private set; }

        public async Task<WarehouseGenerationResult> GenerateAsync(
            WarehouseGenerationInput input,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return await inner.GenerateAsync(input, cancellationToken);
        }
    }
}
