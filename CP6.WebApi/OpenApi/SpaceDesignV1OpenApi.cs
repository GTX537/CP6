using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CP6.WebApi.OpenApi;

public static class SpaceDesignV1OpenApi
{
    public const string DocumentName = "space-design-v1";

    public static void Configure(SwaggerGenOptions options)
    {
        options.CustomOperationIds(
            description =>
                IsContractController(description) &&
                description.TryGetMethodInfo(out var methodInfo)
                    ? methodInfo.Name
                    : null);
        options.CustomSchemaIds(GetSchemaId);
        options.OperationFilter<SpaceDesignV1OperationFilter>();
        options.SwaggerDoc(
            "v1",
            new OpenApiInfo
            {
                Title = "CP6 API",
                Version = "v1",
            });
        options.SwaggerDoc(
            DocumentName,
            new OpenApiInfo
            {
                Title = "CP6 Space Design API",
                Version = "v1",
                Description =
                    "Authoritative OpenAPI contract for Space Design API v1.",
            });
        options.DocInclusionPredicate(
            (documentName, description) =>
                documentName == "v1" ||
                (documentName == DocumentName &&
                 IsContractController(description)));
    }

    private static bool IsContractController(
        Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescription description) =>
        description.ActionDescriptor.RouteValues["controller"] ==
        "SpaceDesignV1";

    private static string GetSchemaId(Type type)
    {
        if (!type.IsGenericType)
            return (type.FullName ?? type.Name).Replace("+", ".");

        var genericName = type.Name.Split('`')[0];
        var argumentNames = string.Join(
            "And",
            type.GetGenericArguments().Select(GetShortSchemaId));
        return $"{genericName}Of{argumentNames}";
    }

    private static string GetShortSchemaId(Type type)
    {
        if (!type.IsGenericType)
            return type.Name;

        var genericName = type.Name.Split('`')[0];
        var argumentNames = string.Join(
            "And",
            type.GetGenericArguments().Select(GetShortSchemaId));
        return $"{genericName}Of{argumentNames}";
    }
}

public sealed class SpaceDesignV1OperationFilter : IOperationFilter
{
    public void Apply(
        OpenApiOperation operation,
        OperationFilterContext context)
    {
        if (operation.OperationId is not (
                "CreateVersion" or
                "CreateSource" or
                "CreateAsset" or
                "AttachUnderlay" or
                "CalibrateUnderlay"))
            return;

        var idempotencyKey = operation.Parameters.Single(
            parameter =>
                parameter.In == ParameterLocation.Header &&
                string.Equals(
                    parameter.Name,
                    "Idempotency-Key",
                    StringComparison.Ordinal));
        idempotencyKey.Required = true;
        idempotencyKey.Description =
            "Opaque caller key; 1-128 UTF-8 bytes. Reuse with a different " +
            "request returns SPACE_IDEMPOTENCY_KEY_REUSED.";

        var successStatus = operation.OperationId switch
        {
            "CreateVersion" => StatusCodes.Status202Accepted.ToString(),
            "AttachUnderlay" or
                "CalibrateUnderlay" =>
                StatusCodes.Status200OK.ToString(),
            _ => StatusCodes.Status201Created.ToString(),
        };
        operation.Responses[successStatus].Headers["Idempotent-Replay"] =
            new OpenApiHeader
            {
                Description =
                    "True when this response replays a stored result.",
                Schema = new OpenApiSchema { Type = "boolean" },
            };
    }
}
