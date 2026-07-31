using System.Text.Json;
using CP6.Space.Contracts;

namespace CP6.Tests.Space;

public sealed class SpaceDesignV1OpenApiTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static readonly string OpenApiPath = Path.Combine(
        RepositoryRoot,
        "docs",
        "space",
        "contracts",
        "design-v1.openapi.json");

    [Fact]
    public void Contract_contains_the_frozen_paths_and_scene_operation()
    {
        using var document = ReadContract();
        var paths = document.RootElement.GetProperty("paths");
        var expectedPaths = new[]
        {
            "/api/space/design/v1/sites/{siteId}/model",
            "/api/space/design/v1/sites/{siteId}/versions",
            "/api/space/design/v1/assets",
            "/api/space/design/v1/versions/{versionId}",
            "/api/space/design/v1/versions/{versionId}/floors/{floorLogicalId}/commands",
            "/api/space/design/v1/versions/{versionId}/floors/{floorLogicalId}/scene",
            "/api/space/design/v1/versions/{versionId}/floors/{floorLogicalId}/underlay",
            "/api/space/design/v1/versions/{versionId}/files/{fileId}",
            "/api/space/design/v1/versions/{versionId}/sources",
            "/api/space/design/v1/versions/{versionId}/sources/{sourceId}/content",
            "/api/space/design/v1/versions/{versionId}/sources/{sourceId}/underlay-calibration",
            "/api/space/design/v1/versions/{versionId}/underlay-sources",
            "/api/space/design/v1/jobs/{jobId}",
            "/api/space/design/v1/versions/{versionId}/issues",
        };

        Assert.Equal(
            expectedPaths.OrderBy(value => value),
            paths.EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(value => value));

        var operationIds = paths.EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Where(IsOperation)
            .Select(operation =>
                operation.Value.GetProperty("operationId").GetString())
            .ToArray();
        Assert.Equal(18, operationIds.Length);
        Assert.Equal(18, operationIds.Distinct().Count());
        Assert.Contains("GetAssets", operationIds);
        Assert.Contains("CreateAsset", operationIds);
        Assert.Contains("CreateVersion", operationIds);
        Assert.Contains("CreateSource", operationIds);
        Assert.Contains("GetScene", operationIds);
        Assert.Contains("ApplyElementCommands", operationIds);
        Assert.Contains("UploadUnderlay", operationIds);
        Assert.Contains("GetFile", operationIds);
        Assert.Contains("GetUnderlayContent", operationIds);
        Assert.Contains("AttachUnderlay", operationIds);
        Assert.Contains("GetUnderlayCalibration", operationIds);
        Assert.Contains("CalibrateUnderlay", operationIds);
    }

    [Fact]
    public void Element_commands_use_a_required_batch_body_and_stable_response()
    {
        using var document = ReadContract();
        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty(
                "/api/space/design/v1/versions/{versionId}/floors/{floorLogicalId}/commands")
            .GetProperty("post");

        Assert.Equal(
            "ApplyElementCommands",
            operation.GetProperty("operationId").GetString());
        Assert.True(
            operation.GetProperty("requestBody").GetProperty("required")
                .GetBoolean());
        Assert.Equal(
            "#/components/schemas/CP6.Space.Contracts.ApplySpaceElementCommandBatchRequest",
            operation.GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString());
        Assert.Equal(
            "#/components/schemas/CP6.Space.Contracts.ApplySpaceElementCommandBatchResponse",
            operation.GetProperty("responses")
                .GetProperty("200")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString());
    }

    [Fact]
    public void Underlay_upload_and_content_use_bounded_binary_contracts()
    {
        using var document = ReadContract();
        var paths = document.RootElement.GetProperty("paths");
        var upload = paths
            .GetProperty(
                "/api/space/design/v1/versions/{versionId}/underlay-sources")
            .GetProperty("post");
        var multipart = upload.GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("multipart/form-data")
            .GetProperty("schema");
        var required = multipart.GetProperty("required")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToHashSet();
        Assert.True(required.SetEquals(["File", "SourceType"]));
        var file = multipart.GetProperty("properties").GetProperty("File");
        Assert.Equal("string", file.GetProperty("type").GetString());
        Assert.Equal("binary", file.GetProperty("format").GetString());

        var content = paths
            .GetProperty(
                "/api/space/design/v1/versions/{versionId}/sources/{sourceId}/content")
            .GetProperty("get")
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content");
        Assert.True(content.TryGetProperty("application/pdf", out _));
        Assert.True(content.TryGetProperty("image/png", out _));
        Assert.True(content.TryGetProperty("image/jpeg", out _));
        foreach (var mediaType in content.EnumerateObject())
        {
            var schema = mediaType.Value.GetProperty("schema");
            Assert.Equal("string", schema.GetProperty("type").GetString());
            Assert.Equal("binary", schema.GetProperty("format").GetString());
        }
    }

    [Fact]
    public void Underlay_attach_requires_body_idempotency_and_replay_header()
    {
        using var document = ReadContract();
        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty(
                "/api/space/design/v1/versions/{versionId}/floors/{floorLogicalId}/underlay")
            .GetProperty("put");

        Assert.True(
            operation.GetProperty("requestBody").GetProperty("required")
                .GetBoolean());
        var idempotency = operation.GetProperty("parameters")
            .EnumerateArray()
            .Single(parameter =>
                parameter.GetProperty("name").GetString() ==
                "Idempotency-Key");
        Assert.True(idempotency.GetProperty("required").GetBoolean());
        Assert.True(
            operation.GetProperty("responses")
                .GetProperty("200")
                .GetProperty("headers")
                .TryGetProperty("Idempotent-Replay", out _));
    }

    [Fact]
    public void Underlay_calibration_requires_floor_body_idempotency_and_replay_header()
    {
        using var document = ReadContract();
        var path = document.RootElement
            .GetProperty("paths")
            .GetProperty(
                "/api/space/design/v1/versions/{versionId}/sources/{sourceId}/underlay-calibration");

        var floorLogicalId = path.GetProperty("get")
            .GetProperty("parameters")
            .EnumerateArray()
            .Single(parameter =>
                parameter.GetProperty("name").GetString() ==
                "floorLogicalId");
        Assert.Equal("query", floorLogicalId.GetProperty("in").GetString());
        Assert.True(floorLogicalId.GetProperty("required").GetBoolean());

        var operation = path.GetProperty("post");
        Assert.True(
            operation.GetProperty("requestBody").GetProperty("required")
                .GetBoolean());
        var idempotency = operation.GetProperty("parameters")
            .EnumerateArray()
            .Single(parameter =>
                parameter.GetProperty("name").GetString() ==
                "Idempotency-Key");
        Assert.True(idempotency.GetProperty("required").GetBoolean());
        Assert.True(
            operation.GetProperty("responses")
                .GetProperty("200")
                .GetProperty("headers")
                .TryGetProperty("Idempotent-Replay", out _));
    }

    [Theory]
    [InlineData(
        "/api/space/design/v1/sites/{siteId}/versions",
        "202")]
    [InlineData(
        "/api/space/design/v1/versions/{versionId}/sources",
        "201")]
    [InlineData(
        "/api/space/design/v1/assets",
        "201")]
    public void Write_operations_require_body_idempotency_and_replay_header(
        string path,
        string successStatus)
    {
        using var document = ReadContract();
        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty(path)
            .GetProperty("post");

        Assert.True(
            operation.GetProperty("requestBody").GetProperty("required")
                .GetBoolean());
        var idempotency = operation.GetProperty("parameters")
            .EnumerateArray()
            .Single(parameter =>
                parameter.GetProperty("name").GetString() ==
                "Idempotency-Key");
        Assert.True(idempotency.GetProperty("required").GetBoolean());
        Assert.True(
            operation.GetProperty("responses")
                .GetProperty(successStatus)
                .GetProperty("headers")
                .TryGetProperty("Idempotent-Replay", out _));
    }

    [Fact]
    public void Error_contract_requires_stable_problem_details_extensions()
    {
        using var document = ReadContract();
        var schema = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("CP6.WebApi.OpenApi.SpaceDesignProblemDetails");
        var required = schema.GetProperty("required")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToHashSet();

        Assert.Contains("code", required);
        Assert.Contains("traceId", required);
        Assert.Contains("correlationId", required);
        Assert.Contains("recovery", required);

        foreach (var path in document.RootElement
                     .GetProperty("paths")
                     .EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject()
                         .Where(IsOperation))
            {
                foreach (var status in new[]
                         {
                             "400", "401", "403", "404", "409", "422", "500",
                         })
                {
                    Assert.True(
                        operation.Value.GetProperty("responses")
                            .GetProperty(status)
                            .GetProperty("content")
                            .TryGetProperty("application/problem+json", out _),
                        $"{operation.Name.ToUpperInvariant()} {path.Name} " +
                        $"does not expose Problem Details for {status}.");
                }
            }
        }
    }

    [Fact]
    public void Generated_clients_use_stable_operation_names()
    {
        var csharp = File.ReadAllText(
            Path.Combine(
                RepositoryRoot,
                "CP6.Space.Client",
                "SpaceDesignV1Client.g.cs"));
        var typescript = File.ReadAllText(
            Path.Combine(
                RepositoryRoot,
                "sdk",
                "typescript",
                "space-design-v1",
                "spaceDesignV1Client.ts"));

        foreach (var operation in new[]
                 {
                     "GetModel",
                     "GetVersions",
                     "CreateVersion",
                     "GetVersion",
                     "GetScene",
                     "ApplyElementCommands",
                     "GetAssets",
                     "CreateAsset",
                     "GetSources",
                     "CreateSource",
                     "UploadUnderlay",
                     "GetFile",
                     "GetUnderlayContent",
                     "AttachUnderlay",
                     "GetUnderlayCalibration",
                     "CalibrateUnderlay",
                     "GetJob",
                     "GetIssues",
                 })
        {
            Assert.Contains(operation, csharp, StringComparison.Ordinal);
            Assert.Contains(operation, typescript, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain("GET2", csharp, StringComparison.Ordinal);
        Assert.DoesNotContain("Dto2", csharp, StringComparison.Ordinal);
    }

    private static JsonDocument ReadContract()
    {
        Assert.True(File.Exists(OpenApiPath), OpenApiPath);
        return JsonDocument.Parse(File.ReadAllText(OpenApiPath));
    }

    private static bool IsOperation(JsonProperty property) =>
        property.Name is "get" or "post" or "put" or "patch" or "delete";
}
