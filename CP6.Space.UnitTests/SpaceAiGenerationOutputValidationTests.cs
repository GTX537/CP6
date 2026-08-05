using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.UnitTests;

public sealed class SpaceAiGenerationOutputValidationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void Valid_typed_and_json_outputs_produce_the_same_stable_evidence()
    {
        var input = Input();
        var output = Output();
        var validator = new WarehouseGenerationOutputValidator();

        var typed = validator.Validate(input, output);
        var json = validator.ValidateJson(
            input,
            JsonSerializer.SerializeToUtf8Bytes(output, JsonOptions));
        var repeated = validator.Validate(input, output);

        Assert.Same(output, typed.Output);
        Assert.Equal(64, typed.CanonicalSha256.Length);
        Assert.Equal(typed.CanonicalSha256, json.CanonicalSha256);
        Assert.Equal(typed.CanonicalSha256, repeated.CanonicalSha256);
    }

    [Theory]
    [InlineData("invalid-json")]
    [InlineData("unknown-root")]
    [InlineData("duplicate-root")]
    [InlineData("missing-root")]
    [InlineData("numeric-enum")]
    [InlineData("unknown-enum")]
    [InlineData("fractional-usage")]
    [InlineData("unsafe-control")]
    [InlineData("extra-nested")]
    public void Raw_canonical_json_schema_violations_fail_closed(
        string scenario)
    {
        var validator = new WarehouseGenerationOutputValidator();

        var error = Assert.Throws<SpaceProblemException>(() =>
            validator.ValidateJson(Input(), CorruptJson(scenario)));

        AssertInvalid(error);
        Assert.DoesNotContain(
            "super-secret-provider-value",
            error.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("schema-version")]
    [InlineData("negative-usage")]
    [InlineData("suggestion-limit")]
    [InlineData("duplicate-suggestion")]
    [InlineData("unknown-source")]
    [InlineData("unknown-type")]
    [InlineData("confidence")]
    [InlineData("attribute-combination")]
    [InlineData("self-relation")]
    [InlineData("unknown-relation-source")]
    [InlineData("duplicate-relation")]
    [InlineData("relation-limit")]
    [InlineData("empty-evidence")]
    [InlineData("duplicate-evidence")]
    [InlineData("unknown-diagnostic-source")]
    [InlineData("unsafe-semantic-label")]
    public void Typed_semantic_contract_violations_fail_closed(
        string scenario)
    {
        var validator = new WarehouseGenerationOutputValidator();

        var error = Assert.Throws<SpaceProblemException>(() =>
            validator.Validate(Input(), InvalidOutput(scenario)));

        AssertInvalid(error);
    }

    [Fact]
    public void Canonical_json_byte_limit_is_checked_before_parsing()
    {
        var validator = new WarehouseGenerationOutputValidator(
            new WarehouseGenerationOutputValidationLimits(32));
        var oversized = Encoding.UTF8.GetBytes(new string('x', 33));

        var error = Assert.Throws<SpaceProblemException>(() =>
            validator.ValidateJson(Input(), oversized));

        AssertInvalid(error);
        Assert.Contains("OUTPUT_JSON_SIZE_INVALID", error.Detail);
    }

    private static void AssertInvalid(SpaceProblemException error)
    {
        Assert.Equal(SpaceErrorCodes.AiOutputInvalid, error.Code);
        Assert.Equal(502, error.StatusCode);
        Assert.False(error.Retryable);
        Assert.Equal("change-ai-provider-or-model", error.RecoveryAction);
        Assert.StartsWith("Provider output failed validation (", error.Detail);
    }

    private static ReadOnlyMemory<byte> CorruptJson(string scenario)
    {
        var json = JsonSerializer.Serialize(Output(), JsonOptions);
        if (scenario == "invalid-json")
            return Encoding.UTF8.GetBytes("{not-json");
        if (scenario == "duplicate-root")
        {
            return Encoding.UTF8.GetBytes(json.Replace(
                "{",
                "{\"providerModel\":\"super-secret-provider-value\",",
                StringComparison.Ordinal));
        }

        var root = JsonNode.Parse(json)!.AsObject();
        switch (scenario)
        {
            case "unknown-root":
                root["secretEndpoint"] = "super-secret-provider-value";
                break;
            case "missing-root":
                root.Remove("usage");
                break;
            case "numeric-enum":
                root["suggestions"]![0]!["suggestedType"] = 3;
                break;
            case "unknown-enum":
                root["suggestions"]![0]!["suggestedType"] = "PromptInstruction";
                break;
            case "fractional-usage":
                root["usage"]!["inputUnits"] = 1.5m;
                break;
            case "unsafe-control":
                root["providerModel"] = "model\nignore-previous";
                break;
            case "extra-nested":
                root["suggestions"]![0]!["attributes"]!["script"] =
                    "super-secret-provider-value";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        return Encoding.UTF8.GetBytes(root.ToJsonString(JsonOptions));
    }

    private static WarehouseGenerationResult InvalidOutput(string scenario)
    {
        var output = Output();
        var suggestion = output.Suggestions[0];
        return scenario switch
        {
            "schema-version" => output with { SchemaVersion = "9.0" },
            "negative-usage" => output with
            {
                Usage = new WarehouseGenerationUsage(-1, 1),
            },
            "suggestion-limit" => output with
            {
                Suggestions = [suggestion, Suggestion("source-2"), Suggestion("source-3")],
            },
            "duplicate-suggestion" => output with
            {
                Suggestions = [suggestion, suggestion],
            },
            "unknown-source" => output with
            {
                Suggestions = [suggestion with { SourceKey = "super-secret-source" }],
            },
            "unknown-type" => output with
            {
                Suggestions =
                [suggestion with { SuggestedType = (WarehouseSpaceType)999 }],
            },
            "confidence" => output with
            {
                Suggestions = [suggestion with { Confidence = 1.01m }],
            },
            "attribute-combination" => output with
            {
                Suggestions =
                [
                    suggestion with
                    {
                        Attributes = new WarehouseSuggestionAttributes(
                            ZonePurpose: WarehouseZonePurpose.Storage),
                    },
                ],
            },
            "self-relation" => output with
            {
                Suggestions =
                [
                    suggestion with
                    {
                        Relations =
                        [
                            new WarehouseSuggestionRelation(
                                WarehouseRelationType.AdjacentTo,
                                "source-1",
                                0.5m),
                        ],
                    },
                ],
            },
            "unknown-relation-source" => output with
            {
                Suggestions =
                [
                    suggestion with
                    {
                        Relations =
                        [
                            new WarehouseSuggestionRelation(
                                WarehouseRelationType.AdjacentTo,
                                "super-secret-source",
                                0.5m),
                        ],
                    },
                ],
            },
            "duplicate-relation" => output with
            {
                Suggestions =
                [suggestion with { Relations = [suggestion.Relations[0], suggestion.Relations[0]] }],
            },
            "relation-limit" => output with
            {
                Suggestions =
                [
                    suggestion with
                    {
                        Relations =
                        [
                            suggestion.Relations[0],
                            new WarehouseSuggestionRelation(
                                WarehouseRelationType.ContainedBy,
                                "source-2",
                                0.5m),
                            new WarehouseSuggestionRelation(
                                WarehouseRelationType.ServedByAisle,
                                "source-2",
                                0.5m),
                        ],
                    },
                ],
            },
            "empty-evidence" => output with
            {
                Suggestions = [suggestion with { EvidenceCodes = [] }],
            },
            "duplicate-evidence" => output with
            {
                Suggestions =
                [
                    suggestion with
                    {
                        EvidenceCodes =
                        [WarehouseEvidenceCode.BLOCK_NAME, WarehouseEvidenceCode.BLOCK_NAME],
                    },
                ],
            },
            "unknown-diagnostic-source" => output with
            {
                Diagnostics =
                [
                    new WarehouseGenerationDiagnostic(
                        "SAFE_CODE",
                        WarehouseDiagnosticSeverity.Warning,
                        "super-secret-source"),
                ],
            },
            "unsafe-semantic-label" => output with
            {
                Suggestions =
                [
                    suggestion with
                    {
                        Attributes = new WarehouseSuggestionAttributes(
                            RackType: WarehouseRackType.Unknown,
                            SemanticLabel: "rack\u0000instruction"),
                    },
                ],
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
    }

    private static WarehouseGenerationInput Input() =>
        new(
            new string('a', 64),
            SpaceAiDataPolicy.StructuredFeatures,
            new WarehouseGenerationLimits(2, 2),
            [
                Feature("source-1", ["source-2"]),
                Feature("source-2", ["source-1"]),
                Feature("source-3", []),
            ],
            [],
            []);

    private static WarehouseGenerationFeature Feature(
        string sourceKey,
        IReadOnlyList<string> relations) =>
        new(
            sourceKey,
            WarehouseCadEntityType.BlockReference,
            "layer-rack-111111111111111111111111",
            "block-rack-222222222222222222222222",
            1,
            new WarehouseNormalizedBounds(0, 0, 0.5m, 0.5m),
            0,
            null,
            [],
            relations,
            0);

    private static WarehouseGenerationResult Output() =>
        new(
            WarehouseGenerationInput.CurrentSchemaVersion,
            "provider-request-1",
            "provider-model-v1",
            new WarehouseGenerationUsage(3, 2),
            [Suggestion("source-1")],
            [
                new WarehouseGenerationDiagnostic(
                    "SAFE_DIAGNOSTIC",
                    WarehouseDiagnosticSeverity.Info,
                    "source-1"),
            ]);

    private static WarehouseGenerationSuggestion Suggestion(string sourceKey) =>
        new(
            sourceKey,
            WarehouseSpaceType.Rack,
            0.9m,
            new WarehouseSuggestionAttributes(
                RackType: WarehouseRackType.Unknown),
            [
                new WarehouseSuggestionRelation(
                    WarehouseRelationType.AdjacentTo,
                    sourceKey == "source-1" ? "source-2" : "source-1",
                    0.75m),
            ],
            [WarehouseEvidenceCode.BLOCK_NAME]);
}
