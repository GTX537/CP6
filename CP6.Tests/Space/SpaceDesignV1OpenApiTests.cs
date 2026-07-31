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
            "/api/space/design/v1/versions/{versionId}/floors/{floorLogicalId}/scene",
            "/api/space/design/v1/versions/{versionId}/sources",
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
            .Where(operation => operation.Name is "get" or "post")
            .Select(operation =>
                operation.Value.GetProperty("operationId").GetString())
            .ToArray();
        Assert.Equal(11, operationIds.Length);
        Assert.Equal(11, operationIds.Distinct().Count());
        Assert.Contains("GetAssets", operationIds);
        Assert.Contains("CreateAsset", operationIds);
        Assert.Contains("CreateVersion", operationIds);
        Assert.Contains("CreateSource", operationIds);
        Assert.Contains("GetScene", operationIds);
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
                         .Where(value => value.Name is "get" or "post"))
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
                     "GetAssets",
                     "CreateAsset",
                     "GetSources",
                     "CreateSource",
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
}
