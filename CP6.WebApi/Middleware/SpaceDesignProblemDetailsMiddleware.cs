using System.Diagnostics;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.WebApi.Localization;
using CP6.WebApi.OpenApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Middleware;

public sealed class SpaceDesignProblemDetailsMiddleware(
    RequestDelegate next,
    ILogger<SpaceDesignProblemDetailsMiddleware> logger)
{
    private const string DesignPath = "/api/space/design/v1";
    private const string ExternalOrganizationPath =
        "/api/space/external-organization";
    private const string FieldPolicyPath = "/api/space/field-policy";
    private const string PortalPath = "/api/space/portal/v1";
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(DesignPath) &&
            !context.Request.Path.StartsWithSegments(
                ExternalOrganizationPath) &&
            !context.Request.Path.StartsWithSegments(FieldPolicyPath) &&
            !context.Request.Path.StartsWithSegments(PortalPath))
        {
            await next(context);
            return;
        }

        try
        {
            await next(context);
            if (context.Response.StatusCode == StatusCodes.Status401Unauthorized &&
                !context.Response.HasStarted)
            {
                await WriteAsync(
                    context,
                    StatusCodes.Status401Unauthorized,
                    SpaceErrorCodes.AuthenticationRequired,
                    "Authentication is required.",
                    null,
                    "authenticate");
            }
            else if (
                context.Response.StatusCode == StatusCodes.Status403Forbidden &&
                !context.Response.HasStarted)
            {
                await WriteAsync(
                    context,
                    StatusCodes.Status403Forbidden,
                    SpaceErrorCodes.PermissionDenied,
                    "The Space request was denied.",
                    null,
                    "request-access");
            }
        }
        catch (OperationCanceledException)
            when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (SpaceProblemException exception)
        {
            await WriteAsync(
                context,
                exception.StatusCode,
                exception.Code,
                exception.Title,
                exception.Detail,
                exception.RecoveryAction,
                exception.Retryable);
        }
        catch (BizException exception)
        {
            var code = exception.Code switch
            {
                "SPACE_TENANT_CONTEXT_REQUIRED" or
                "SPACE_ACTOR_CONTEXT_REQUIRED" =>
                    SpaceErrorCodes.TenantScopeDenied,
                _ => exception.Code,
            };
            await WriteAsync(
                context,
                exception.HttpStatus,
                code,
                exception.HttpStatus == 401
                    ? "Authentication is required."
                    : "The Space request scope was denied.",
                null,
                exception.HttpStatus == 401
                    ? "authenticate"
                    : "reauthenticate");
        }
        catch (SpaceTenantScopeException exception)
        {
            await WriteAsync(
                context,
                403,
                SpaceErrorCodes.TenantScopeDenied,
                "The Space tenant scope was denied.",
                exception.Message,
                "reauthenticate");
        }
        catch (SpaceVersionConflictException exception)
        {
            await WriteAsync(
                context,
                409,
                SpaceErrorCodes.VersionConflict,
                "The Space version changed concurrently.",
                exception.Message,
                "reload-current-version");
        }
        catch (SpaceVersionStateException exception)
        {
            await WriteAsync(
                context,
                409,
                SpaceErrorCodes.VersionStateInvalid,
                "The Space version state does not allow this operation.",
                exception.Message,
                "reload-current-version");
        }
        catch (SpaceExternalAccessStateException exception)
        {
            await WriteAsync(
                context,
                409,
                SpaceErrorCodes.ExternalAccessStateInvalid,
                "The external access state does not allow this operation.",
                exception.Message,
                "reload-current-resource");
        }
        catch (SpaceFileValidationException exception)
        {
            await WriteAsync(
                context,
                422,
                exception.Code,
                "The Space source file is not safe to use.",
                exception.Message,
                "upload-and-scan-source");
        }
        catch (SpaceFileStateException exception)
        {
            await WriteAsync(
                context,
                422,
                SpaceErrorCodes.SourceUnsafe,
                "The Space source file is not safe to use.",
                exception.Message,
                "upload-and-scan-source");
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await WriteAsync(
                context,
                409,
                SpaceErrorCodes.ConcurrencyConflict,
                "The Space resource changed concurrently.",
                "Reload the current state before retrying.",
                "reload-current-resource");
            logger.LogInformation(
                exception,
                "A Design API optimistic concurrency check failed.");
        }
        catch (ArgumentException exception)
        {
            await WriteAsync(
                context,
                400,
                SpaceErrorCodes.RequestInvalid,
                "The request is invalid.",
                exception.Message,
                "correct-request");
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unhandled Design API failure for {Path}.",
                context.Request.Path);
            await WriteAsync(
                context,
                500,
                "SPACE_INTERNAL_ERROR",
                "The Space request could not be completed.",
                null,
                "contact-support",
                retryable: false);
        }
    }

    internal static ObjectResult CreateResult(
        HttpContext context,
        int status,
        string code,
        string title,
        string? detail,
        string recoveryAction,
        bool retryable = false)
    {
        var problem = CreateProblem(
            context,
            status,
            code,
            title,
            detail,
            recoveryAction,
            retryable);
        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" },
        };
    }

    private static async Task WriteAsync(
        HttpContext context,
        int status,
        string code,
        string title,
        string? detail,
        string recoveryAction,
        bool retryable = false)
    {
        if (context.Response.HasStarted)
        {
            throw new InvalidOperationException(
                "The Design API response has already started.");
        }

        var problem = CreateProblem(
            context,
            status,
            code,
            title,
            detail,
            recoveryAction,
            retryable);
        var authenticate = context.Response.Headers.WWWAuthenticate.ToArray();
        context.Response.Clear();
        if (authenticate.Length > 0)
            context.Response.Headers.WWWAuthenticate = authenticate;
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            problem,
            JsonOptions,
            context.RequestAborted);
    }

    private static SpaceDesignProblemDetails CreateProblem(
        HttpContext context,
        int status,
        string code,
        string title,
        string? detail,
        string recoveryAction,
        bool retryable) =>
        new()
        {
            Type = $"https://cp6.example/problems/{ToSlug(code)}",
            Title = title,
            Status = status,
            Detail = detail,
            Instance = context.Request.Path,
            Code = code,
            TraceId =
                context.Response.Headers["X-Trace-ID"].FirstOrDefault()
                ?? Activity.Current?.TraceId.ToHexString()
                ?? context.TraceIdentifier,
            CorrelationId =
                context.Response.Headers["X-Correlation-ID"].FirstOrDefault()
                ?? context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
                ?? string.Empty,
            Recovery = new SpaceRecoveryDetails(
                recoveryAction,
                retryable),
        };

    private static string ToSlug(string code) =>
        code.ToLowerInvariant().Replace('_', '-');
}
