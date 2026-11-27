using System.Text.Json;
using CP6.Core.Services.ErpIntegration;
using CP6.Core.Services.Integration;
using static CP6.Core.Services.Plm.EngineeringIntakeWireV1;
using static CP6.Core.Services.Plm.EngineeringIntakeStoreV1;

namespace CP6.Core.Services.Plm;

public sealed partial class EngineeringIntakeContextService
{
    private async Task<(EngineeringIntakeRefV1?, Guid?, long?)> WriteRequirementAsync(OperationsAccessV1 access, EngineeringRequirementStoreV1 native,
        EngineeringIntakeRouteV1 route, EngineeringIntakeRequestV1 request, byte[] sourceBytes, Current? current,
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
        EngineeringRequirementSnapshotV1 saved;
        if (current?.Target is not { } target)
        {
            var body = new EngineeringRequirementBodyV1("REQUIREMENT", "CRM 工程交接需求",
                Requirement: new(sourceSnapshot.Reference, clauses.Select(clause => clause.Id).ToArray(), "待工程分析与确认",
                    "", "", request.Source.Unknowns, [], []));
            saved = await CreateAsync(add, native, body, route.DepartmentId, request.RequestReason, ct);
        }
        else
        {
            var prior = current.Adopted ?? throw OriginalUnavailable();
            var successor = ReviseDefinition(target, prior.Original, request.Source, sourceSnapshot.Reference);
            var edit = await access.ActionAsync(OperationsOwnerV1.EngineeringRequirement, "edit", ct);
            var revise = new EngineeringRequirementCommandV1
            {
                OperationId = Guid.NewGuid(), ObjectId = target.Reference.Id, Action = "REVISE",
                ExpectedVersion = target.Reference.Version, ExpectedEtag = target.Etag, Reason = request.RequestReason,
                Body = target.Body with { Requirement = successor }
            };
            saved = await requirements.WriteWithinAsync(edit, native, revise, EngineeringRequirementService.ValidateCommand(revise), ct,
                fromEngineeringIntake: true);
        }
        return (Reference(saved), sourceSnapshot.Reference.Id, CommercialWireV1.Revision(sourceSnapshot.Reference.Version));
    }

    private static RequirementDefinitionV1 ReviseDefinition(EngineeringRequirementSnapshotV1 target, EngineeringIntakeSourceV1 adopted,
        EngineeringIntakeSourceV1 incoming, EngineeringSourceRefV1 source)
    {
        var previous = target.Body.Requirement ?? throw OriginalUnavailable();
        var before = SourceClauses(adopted);
        var after = SourceClauses(incoming);
        var changed = before.Keys.Union(after.Keys, StringComparer.Ordinal).Where(id =>
            !before.TryGetValue(id, out var oldValue) || !after.TryGetValue(id, out var newValue) || !JsonElement.DeepEquals(oldValue, newValue))
            .ToHashSet(StringComparer.Ordinal);
        var clauseIds = previous.ClauseIds.Where(after.ContainsKey).ToList();
        if (previous.ClauseIds.Length == before.Count)
            clauseIds.AddRange(after.Keys.Where(id => !before.ContainsKey(id)));
        if (clauseIds.Count == 0) clauseIds.Add("summary");
        var selected = clauseIds.ToHashSet(StringComparer.Ordinal);
        var incomingContext = SourceContext(incoming);
        var changedSourceFields = SourceContext(adopted).EnumerateObject()
            .Where(field => !JsonElement.DeepEquals(field.Value, incomingContext.GetProperty(field.Name)))
            .Select(field => field.Name).ToArray();
        var contextChanged = changedSourceFields.Length > 0;
        var scopeChanged = contextChanged || previous.ClauseIds.Any(changed.Contains) || clauseIds.Any(changed.Contains);

        bool Affected(IEnumerable<string>? basis) => contextChanged || (basis?.Any() == true ? basis.Any(changed.Contains) : scopeChanged);
        var criterionIds = previous.Criteria.Where(c => Affected(c.SourceClauseIds)).Select(c => c.Id).ToArray();
        var relationIds = previous.Relations.Where(r => Affected(r.Coverage?.Select(link => link.ChildClauseId))).Select(r => r.Id).ToArray();
        var fields = new List<string>();
        if (scopeChanged)
        {
            fields.Add("interpretation");
            if (previous.ApplicableObject.Length > 0) fields.Add("applicableObject");
            if (previous.Necessity.Length > 0) fields.Add("necessity");
        }
        var questions = previous.ClarificationQuestions;
        if (!adopted.Unknowns.SequenceEqual(incoming.Unknowns, StringComparer.Ordinal))
        {
            fields.Add("clarificationQuestions");
            var merged = questions.Except(adopted.Unknowns, StringComparer.Ordinal).Union(incoming.Unknowns, StringComparer.Ordinal).ToArray();
            // 已满的工程草稿继续保留原有问题，新 unknowns 保存在本次准确来源原件中。
            if (merged.Length <= 256) questions = merged;
        }
        // 覆盖评审固定准确父版及其旧来源，后继保留复核依据。
        if (previous.Decomposition is not null) fields.Add("decomposition");
        var reviews = previous.SourceReviews;
        if (fields.Count > 0 || criterionIds.Length > 0 || relationIds.Length > 0)
            reviews = [.. (reviews ?? []), new(target.Reference, previous.Source, source, changed.Order(StringComparer.Ordinal).ToArray(),
                changedSourceFields, fields.ToArray(), criterionIds, relationIds, "REVIEW_REQUIRED")];

        // 原条款被来源移除时，原绑定继续由待重评记录所指的前版提供。
        var criteria = previous.Criteria.Select(criterion => criterion.SourceClauseIds is { } basis && basis.Any(id => !selected.Contains(id))
            ? criterion with { SourceClauseIds = basis.Where(selected.Contains).ToArray() } : criterion).ToArray();
        var relations = previous.Relations.Select(relation =>
        {
            if (relation.Coverage is not { } coverage || coverage.All(link => selected.Contains(link.ChildClauseId))) return relation;
            var retained = coverage.Where(link => selected.Contains(link.ChildClauseId)).ToArray();
            return relation with { Coverage = retained.Length == 0 ? null : retained };
        }).ToArray();
        return previous with
        {
            Source = source, ClauseIds = clauseIds.ToArray(), ClarificationQuestions = questions, Criteria = criteria, Relations = relations,
            Decomposition = null, SourceReviews = reviews
        };
    }

    private static Dictionary<string, JsonElement> SourceClauses(EngineeringIntakeSourceV1 source)
    {
        var clauses = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["summary"] = source.Requirement.GetProperty("summary") };
        foreach (var line in source.Requirement.GetProperty("lines").EnumerateArray())
            clauses.Add(line.GetProperty("lineId").GetString()!, line);
        return clauses;
    }

    private static JsonElement SourceContext(EngineeringIntakeSourceV1 source)
    {
        var canonical = CanonicalSource(source);
        return JsonSerializer.SerializeToElement(new
        {
            canonical.AccountId, canonical.CandidateProduct, canonical.KnownConstraints, canonical.Unknowns,
            canonical.ActivitySources, canonical.EvidenceRefs,
            decisionPath = source.Requirement.GetProperty("decisionPath"),
            noDecisionDateReason = source.Requirement.GetProperty("noDecisionDateReason")
        }, WireJson);
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
