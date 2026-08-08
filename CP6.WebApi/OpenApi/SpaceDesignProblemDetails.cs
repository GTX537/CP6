using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.OpenApi;

public sealed class SpaceDesignProblemDetails : ProblemDetails
{
    [Required]
    public required string Code { get; init; }

    [Required]
    public required string TraceId { get; init; }

    [Required]
    public required string CorrelationId { get; init; }

    [Required]
    public required SpaceRecoveryDetails Recovery { get; init; }
}

public sealed record SpaceRecoveryDetails(
    [property: Required] string Action,
    bool Retryable);
