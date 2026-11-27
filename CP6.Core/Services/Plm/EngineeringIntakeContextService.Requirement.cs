using CP6.Core.Services.ErpIntegration;
using CP6.Core.Services.Integration;
using static CP6.Core.Services.Plm.EngineeringIntakeWireV1;
using static CP6.Core.Services.Plm.EngineeringIntakeStoreV1;

namespace CP6.Core.Services.Plm;

public sealed partial class EngineeringIntakeContextService
{
    private async Task<(EngineeringIntakeRefV1?, Guid?, long?)> WriteRequirementAsync(OperationsAccessV1 access, EngineeringRequirementStoreV1 native,
        EngineeringIntakeRouteV1 route, EngineeringIntakeRequestV1 request, byte[] sourceBytes, EngineeringRequirementSnapshotV1? target,
        CancellationToken ct)
    {
        var source = request.Source.Requirement;
        var clauses = new List<RequirementClauseV1>
        {
            new("summary", source.GetProperty("summary").GetString()!, "JSON:/requirement/summary")
        };
        var index = 0;
        foreach (var line in source.GetProperty("lines").EnumerateArray())
        {
            clauses.Add(new(line.GetProperty("lineId").GetString()!, line.GetProperty("description").GetString()!,
                "JSON:/requirement/lines/" + Version(index) + "/description"));
            index++;
        }
        var origin = new RequirementOriginV1(new(SourceOwner, request.RequestKey, request.SourceBinding.SourceVersion, Hash(sourceBytes)), null);
        var sourceBody = new EngineeringRequirementBodyV1("SOURCE", "CRM 工程交接来源",
            new("OWNER_ORIGINAL", "CRM 商机需求原件", request.SourceBinding.SourceVersion, "Main 工程受理原键记录", clauses.ToArray(), origin));
        var add = await access.ActionAsync(OperationsOwnerV1.EngineeringRequirement, "add", ct);
        var sourceSnapshot = await CreateAsync(add, native, sourceBody, route.DepartmentId, request.RequestReason, ct);
        var body = new EngineeringRequirementBodyV1("REQUIREMENT", target?.Body.Name ?? "CRM 工程交接需求",
            Requirement: new(sourceSnapshot.Reference, clauses.Select(clause => clause.Id).ToArray(), "待工程分析与确认",
                "", "", request.Source.Unknowns, [], [], Clarifications: target?.Body.Requirement?.Clarifications));
        EngineeringRequirementSnapshotV1 saved;
        if (target is null)
            saved = await CreateAsync(add, native, body, route.DepartmentId, request.RequestReason, ct);
        else
        {
            var edit = await access.ActionAsync(OperationsOwnerV1.EngineeringRequirement, "edit", ct);
            var revise = new EngineeringRequirementCommandV1
            {
                OperationId = Guid.NewGuid(), ObjectId = target.Reference.Id, Action = "REVISE",
                ExpectedVersion = target.Reference.Version, ExpectedEtag = target.Etag, Reason = request.RequestReason, Body = body
            };
            saved = await requirements.WriteWithinAsync(edit, native, revise, EngineeringRequirementService.ValidateCommand(revise), ct);
        }
        return (Reference(saved), sourceSnapshot.Reference.Id, CommercialWireV1.Revision(sourceSnapshot.Reference.Version));
    }

    private async Task<EngineeringRequirementSnapshotV1> CreateAsync(OperationsAccessV1 access, EngineeringRequirementStoreV1 store,
        EngineeringRequirementBodyV1 body, Guid? department, string reason, CancellationToken ct)
    {
        var permission = await access.EngineeringRequirementCreationContext(ct);
        var command = new EngineeringRequirementCommandV1
        {
            OperationId = Guid.NewGuid(), ObjectId = Guid.NewGuid(), Action = "CREATE",
            ExpectedAuthorityVersion = permission.AuthorityVersion, DepartmentId = department, Reason = reason, Body = body
        };
        return await requirements.WriteWithinAsync(access, store, command, EngineeringRequirementService.ValidateCommand(command), ct);
    }

    private static EngineeringIntakeResultV1 CreateIntake(EngineeringIntakeRouteV1 route, EngineeringIntakeRequestV1 request,
        EngineeringIntakeRefV1? engineering, string[] missing, DateTimeOffset now)
    {
        var stream = route.IntakeStream;
        var receipt = Guid.NewGuid().ToString("D");
        var result = new EngineeringIntakeResultV1(IntakeContract, request.TenantId, Guid.NewGuid().ToString("D"),
            stream.Producer, stream.Epoch, ResultSource(request), new("1", null, null), receipt,
            missing.Length == 0 ? "ACCEPTED" : "NEEDS_INFO", engineering, missing,
            missing.Length == 0 ? null : "REQUIRED_ENGINEERING_INPUT_PENDING",
            new(route.Scope.Authority, receipt, route.OwnerSigner.Version, ""), now, "ORDINARY", null);
        return result with { OwnerEvidence = result.OwnerEvidence with { ProofRef = route.OwnerSigner.Sign("HO-ENG-INTAKE-EVIDENCE-1", result) } };
    }

    private static EngineeringIntakeTrustV1 SignTrust(EngineeringIntakeRouteV1 route, string credential, Guid requestKey,
        string contract, string producer, string epoch, string bodyHash, DateTimeOffset now)
    {
        var trust = new EngineeringIntakeTrustV1(route.Scope.Authority, route.Scope.TenantId, producer, contract, epoch, credential, "", now);
        return trust with
        {
            VerificationRef = route.OwnerSigner.Sign("HO-ENG-ORIGINAL-TRUST-1", new { requestKey, bodyHash, trust })
        };
    }
}
