using CP6.Core.Services.Space.Observability;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Space;

public sealed class AuditedSpaceAccessEvaluatorTests
{
    private static readonly DateTime Now =
        new(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task External_export_attempt_persists_authorization_evidence()
    {
        await using var fixture = await Fixture.CreateAsync(
            canExport: true,
            auditAvailable: true);

        var decision = await fixture.Evaluator.EvaluateAsync(
            fixture.Principal,
            SpaceAccessAction.Export,
            fixture.Resource);

        Assert.True(decision.Allowed);
        var input = Assert.Single(fixture.Writer.Inputs);
        Assert.Equal("space.external.export.attempt", input.Action);
        Assert.Equal(SpaceAuditOutcome.Succeeded, input.Outcome);
        Assert.Equal(fixture.SiteId, input.SiteId);
        Assert.Equal("Allowed", input.Evidence!.AuthorizationResult);
        Assert.Equal(fixture.OrganizationId, input.Evidence.OrganizationId);
        Assert.Equal(decision.Scope.AuthorizationVersion,
            input.Evidence.AuthorizationVersion);
        Assert.Equal(decision.MatchedGrantIds, input.Evidence.GrantIds);
        Assert.Equal(decision.FieldPolicyIds, input.Evidence.FieldPolicyIds);
    }

    [Fact]
    public async Task Denied_external_export_attempt_is_still_audited()
    {
        await using var fixture = await Fixture.CreateAsync(
            canExport: false,
            auditAvailable: true);

        var decision = await fixture.Evaluator.EvaluateAsync(
            fixture.Principal,
            SpaceAccessAction.Export,
            fixture.Resource);

        Assert.False(decision.Allowed);
        Assert.Equal(SpaceErrorCodes.ExternalScopeDenied, decision.ReasonCode);
        var input = Assert.Single(fixture.Writer.Inputs);
        Assert.Equal(SpaceAuditOutcome.Denied, input.Outcome);
        Assert.Equal(decision.ReasonCode, input.ReasonCode);
        Assert.Equal("Denied", input.Evidence!.AuthorizationResult);
    }

    [Fact]
    public async Task Export_authorization_fails_closed_when_audit_is_unavailable()
    {
        await using var fixture = await Fixture.CreateAsync(
            canExport: true,
            auditAvailable: false);

        var decision = await fixture.Evaluator.EvaluateAsync(
            fixture.Principal,
            SpaceAccessAction.Export,
            fixture.Resource);

        Assert.False(decision.Allowed);
        Assert.Equal(SpaceErrorCodes.AuditUnavailable, decision.ReasonCode);
        Assert.Empty(decision.MatchedGrantIds);
        Assert.Empty(decision.FieldPolicyIds);
        Assert.Single(fixture.Writer.Inputs);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            SpaceContext context,
            AuditedSpaceAccessEvaluator evaluator,
            RecordingWriter writer,
            SpacePrincipal principal,
            SpaceResource resource,
            Guid organizationId,
            Guid siteId)
        {
            Context = context;
            Evaluator = evaluator;
            Writer = writer;
            Principal = principal;
            Resource = resource;
            OrganizationId = organizationId;
            SiteId = siteId;
        }

        private SpaceContext Context { get; }
        public AuditedSpaceAccessEvaluator Evaluator { get; }
        public RecordingWriter Writer { get; }
        public SpacePrincipal Principal { get; }
        public SpaceResource Resource { get; }
        public Guid OrganizationId { get; }
        public Guid SiteId { get; }

        public static async Task<Fixture> CreateAsync(
            bool canExport,
            bool auditAvailable)
        {
            var tenantId = Guid.NewGuid();
            var actorId = Guid.NewGuid();
            var siteId = Guid.NewGuid();
            var organization = SpaceExternalOrganization.Create(
                tenantId,
                SpaceExternalOrganizationType.Customer,
                "CUST-A",
                "Customer A");
            var execution = new ExternalExecution(
                tenantId,
                actorId,
                organization.Id);
            var clock = new FixedClock();
            var context = new SpaceContext(
                new DbContextOptionsBuilder<SpaceContext>()
                    .UseInMemoryDatabase(
                        Guid.NewGuid().ToString("N"))
                    .Options,
                execution,
                clock);
            var membership = SpaceExternalMembership.Create(
                tenantId,
                organization.Id,
                actorId,
                SpaceExternalMembershipRole.Viewer,
                Now.AddDays(-1),
                null,
                SpaceExternalMembershipStatus.Active,
                null,
                Now);
            var policy = SpaceFieldPolicy.Create(
                tenantId,
                "Stock export",
                SpaceExternalOrganizationType.Customer,
                canExport: true);
            var grant = SpaceExternalGrant.Create(
                tenantId,
                organization.Id,
                siteId,
                policy.Id,
                canExport,
                Now.AddDays(-1),
                null,
                SpaceExternalGrantStatus.Active);
            context.AddRange(organization, membership, policy, grant);
            await context.SaveChangesAsync();

            var writer = new RecordingWriter(auditAvailable);
            var http = new DefaultHttpContext();
            http.Connection.RemoteIpAddress =
                System.Net.IPAddress.Parse("192.0.2.10");
            http.Request.Headers.UserAgent = "cp6-test";
            var evaluator = new AuditedSpaceAccessEvaluator(
                new SpaceAccessEvaluator(context, execution, clock),
                writer,
                new HttpContextAccessor { HttpContext = http });
            var principal = new SpacePrincipal(
                tenantId,
                actorId,
                true,
                organization.Id);
            return new Fixture(
                context,
                evaluator,
                writer,
                principal,
                new SpaceResource(
                    tenantId,
                    SpaceResourceType.Stock,
                    siteId),
                organization.Id,
                siteId);
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed record ExternalExecution(
        Guid TenantId,
        Guid ActorId,
        Guid? OrganizationContextId) :
        CP6.Space.Application.ISpaceExecutionContext
    {
        public bool IsExternal => true;
    }

    private sealed class FixedClock : ISpaceClock
    {
        public DateTime UtcNow => Now;
    }

    public sealed class RecordingWriter(bool result) : ISpaceAuditWriter
    {
        public List<SpaceAuditEventInput> Inputs { get; } = [];

        public Task<bool> TryAppendAsync(
            SpaceAuditEventInput input,
            CancellationToken ct = default)
        {
            Inputs.Add(input);
            return Task.FromResult(result);
        }
    }
}
