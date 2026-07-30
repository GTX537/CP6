using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.Services.Space.Compatibility;
using CP6.Space.Application;
using CP6.Space.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using CoreExecutionContext =
    CP6.Core.Services.Space.Observability.ISpaceExecutionContext;
using CoreExecutionContextAccessor =
    CP6.Core.Services.Space.Observability.ISpaceExecutionContextAccessor;

namespace CP6.WebApi.Services;

public sealed class HttpSpaceApplicationExecutionContext(
    CoreExecutionContextAccessor accessor) :
    CP6.Space.Application.ISpaceExecutionContext,
    ISpaceCorrelationContext
{
    private CoreExecutionContext? Current => accessor.Current;

    public Guid TenantId => Current?.TenantId ?? Guid.Empty;

    public Guid ActorId =>
        Guid.TryParse(Current?.ActorId, out var actorId)
            ? actorId
            : Guid.Empty;

    public Guid CorrelationId => Current?.CorrelationId ?? Guid.Empty;
}

public sealed class CompatibilitySpaceDesignAccessEvaluator(
    CP6.Space.Application.ISpaceExecutionContext execution,
    IOptions<SpaceCompatibilityOptions> options) :
    ISpaceDesignAccessEvaluator
{
    private readonly SpaceCompatibilityOptions _options = options.Value;

    public void EnsureSiteAccess(Guid siteId, bool write)
    {
        var site = _options.Sites.SingleOrDefault(
            candidate =>
                candidate.TenantId == execution.TenantId &&
                candidate.SiteId == siteId);
        if (!_options.DesignApiEnabled ||
            site is null ||
            site.Mode != SpaceSiteMode.DesignV1 ||
            site.CutoverState != SpaceCutoverState.DesignV1 ||
            !site.Evidence.IsVerified)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.DesignApiDisabled,
                404,
                "The Design API is not enabled for this Site.",
                recoveryAction: "use-legacy-api");
        }
    }
}

public sealed class DataProtectionSpaceCursorCodec : ISpaceCursorCodec
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IDataProtector _protector;
    private readonly CoreExecutionContextAccessor _execution;
    private readonly IHttpContextAccessor _http;
    private readonly ISpaceClock _clock;

    public DataProtectionSpaceCursorCodec(
        IDataProtectionProvider provider,
        CoreExecutionContextAccessor execution,
        IHttpContextAccessor http,
        ISpaceClock clock)
    {
        _protector = provider.CreateProtector(
            "CP6.Space.DesignV1.Cursor.v1");
        _execution = execution;
        _http = http;
        _clock = clock;
    }

    public string Encode(SpaceCursorState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var current = _execution.RequireCurrent();
        var envelope = new CursorEnvelope(
            current.TenantId,
            current.ActorId,
            current.OrganizationContextId,
            GrantVersion(),
            state.Resource,
            state.FilterHash,
            state.Offset,
            RequireUtcNow().AddMinutes(15));
        return _protector.Protect(
            JsonSerializer.Serialize(envelope, JsonOptions));
    }

    public SpaceCursorState Decode(
        string cursor,
        string expectedResource,
        string expectedFilterHash)
    {
        CursorEnvelope envelope;
        try
        {
            var json = _protector.Unprotect(cursor);
            envelope = JsonSerializer.Deserialize<CursorEnvelope>(
                           json,
                           JsonOptions)
                       ?? throw new CryptographicException();
        }
        catch (Exception exception)
            when (exception is CryptographicException or JsonException)
        {
            throw InvalidCursor();
        }

        var current = _execution.RequireCurrent();
        if (envelope.TenantId != current.TenantId ||
            !string.Equals(
                envelope.ActorId,
                current.ActorId,
                StringComparison.Ordinal) ||
            !string.Equals(
                envelope.OrganizationContextId,
                current.OrganizationContextId,
                StringComparison.Ordinal) ||
            !string.Equals(
                envelope.GrantVersion,
                GrantVersion(),
                StringComparison.Ordinal) ||
            !string.Equals(
                envelope.Resource,
                expectedResource,
                StringComparison.Ordinal) ||
            !string.Equals(
                envelope.FilterHash,
                expectedFilterHash,
                StringComparison.Ordinal))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.CursorScopeMismatch,
                400,
                "The cursor does not belong to this request scope.",
                recoveryAction: "restart-pagination");
        }
        if (envelope.ExpiresAtUtc <= RequireUtcNow() ||
            envelope.Offset < 0)
            throw InvalidCursor();

        return new SpaceCursorState(
            envelope.Resource,
            envelope.FilterHash,
            envelope.Offset);
    }

    private string GrantVersion() =>
        _http.HttpContext?.User.FindFirst("space_grant_version")?.Value
        ?? "internal-v1";

    private DateTime RequireUtcNow()
    {
        var now = _clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException(
                "The Space clock must return UTC.");
        }
        return now;
    }

    private static SpaceProblemException InvalidCursor() =>
        new(
            SpaceErrorCodes.CursorInvalid,
            400,
            "The cursor is invalid or expired.",
            recoveryAction: "restart-pagination");

    private sealed record CursorEnvelope(
        Guid TenantId,
        string ActorId,
        string? OrganizationContextId,
        string GrantVersion,
        string Resource,
        string FilterHash,
        int Offset,
        DateTime ExpiresAtUtc);
}
