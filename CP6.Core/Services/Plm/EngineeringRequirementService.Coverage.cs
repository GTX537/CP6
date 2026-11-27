using System.Text.Json;
using CP6.Core.Services.Integration;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;

public sealed partial class EngineeringRequirementService
{
    private static async Task ValidateClarifications(OperationsAccessV1 access, EngineeringRequirementStoreV1 store,
        RequirementDefinitionV1 definition, RequirementDefinitionV1? previous, CancellationToken ct)
    {
        var prior = (previous?.Clarifications ?? []).ToDictionary(item => item.Id);
        var current = (definition.Clarifications ?? []).ToDictionary(item => item.Id);
        foreach (var (id, question) in prior)
        {
            if (!current.TryGetValue(id, out var revised) || revised with { Reply = question.Reply } != question
                || question.Reply != null && revised.Reply != question.Reply)
                throw Error("REQUIREMENT_CLARIFICATION_IMMUTABLE", 422);
        }
        foreach (var question in current.Values)
        {
            if (!prior.ContainsKey(question.Id))
            {
                if (question.Source != definition.Source || !definition.ClauseIds.Contains(question.ClauseId, StringComparer.Ordinal))
                    throw Error("REQUIREMENT_CLARIFICATION_SOURCE_MISMATCH", 422);
                if (question.Reply is not null) throw Error("REQUIREMENT_CLARIFICATION_NOT_REQUESTED", 422);
            }
            var source = await Exact(access, store, question.Source, "SOURCE", ct);
            if (!source.Body.Source!.Clauses.Any(clause => clause.Id == question.ClauseId))
                throw Error("REQUIREMENT_CLARIFICATION_SOURCE_MISMATCH", 422);
            if (question.Reply != null && question.Reply == source.Body.Source.Origin)
                throw Error("REQUIREMENT_CLARIFICATION_REPLY_IS_ORIGINAL", 422);
        }
    }

    private static async Task<RequirementCoverageV1> ReadCoverage(OperationsAccessV1 access,
        EngineeringRequirementStoreV1 store, RequirementDefinitionV1 definition,
        RequirementDecompositionV1 review, Func<EngineeringRequirementSnapshotV1, Task> visit, CancellationToken ct)
    {
        var parent = await Exact(access, store, review.Parent, "REQUIREMENT", ct);
        var baseline = parent.Body.Requirement!;
        if (baseline.Source != definition.Source || !baseline.ClauseIds.SequenceEqual(definition.ClauseIds) ||
            baseline.ApplicableObject != definition.ApplicableObject)
            throw Error("REQUIREMENT_COVERAGE_PARENT_CHANGED", 412);
        await visit(parent);
        var parentSource = await Exact(access, store, baseline.Source, "SOURCE", ct);
        var parentClauses = parentSource.Body.Source!.Clauses.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var evidence = baseline.ClauseIds.ToDictionary(id => id, _ => new List<RequirementCoverageEvidenceV1>(), StringComparer.Ordinal);
        foreach (var reference in review.Children)
        {
            if (reference.Id == review.Parent.Id) throw Error("REQUIREMENT_RELATION_CYCLE", 422);
            var child = await Exact(access, store, reference, "REQUIREMENT", ct);
            await visit(child);
            var defined = child.Body.Requirement!;
            var links = defined.Relations.Where(relation => relation.Kind == "PARENT" && relation.Target == review.Parent)
                .SelectMany(relation => relation.Coverage ?? []).ToArray();
            if (links.Length==0) throw Error("REQUIREMENT_COVERAGE_CHILD_UNLINKED", 422);
            var childSource = await Exact(access, store, defined.Source, "SOURCE", ct);
            var childClauses = childSource.Body.Source!.Clauses.ToDictionary(c => c.Id, StringComparer.Ordinal);
            foreach (var link in links)
            {
                if (!evidence.TryGetValue(link.ParentClauseId, out var clauseEvidence) ||
                    !defined.ClauseIds.Contains(link.ChildClauseId, StringComparer.Ordinal))
                    throw Error("REQUIREMENT_COVERAGE_CLAUSE_INVALID", 422);
                if (clauseEvidence.Any(item => item.Child == reference && item.ChildClauseId == link.ChildClauseId))
                    throw Error("REQUIREMENT_COVERAGE_DUPLICATE_LINK", 422);
                var terms = childClauses[link.ChildClauseId].Terms;
                var scope = RequirementApplicabilityV1.Coverage(parentClauses[link.ParentClauseId].Terms, terms);
                clauseEvidence.Add(new(reference, link.ChildClauseId, link.Reason, scope, terms));
            }
        }
        var results = evidence.Select(pair =>
        {
            var items = pair.Value.ToArray();
            var state = items.Any(item => item.Scope == "COVERED") ? "COVERED"
                : items.Length == 0 ? "MISSING"
                : items.Any(item => item.Scope is "UNKNOWN" or "NECESSITY_UNPROVEN") ? "UNKNOWN"
                : items.All(item => item.Scope == "DISJOINT") ? "DISJOINT" : "PARTIAL";
            return new RequirementClauseCoverageV1(pair.Key, state, items, parentClauses[pair.Key].Terms);
        }).ToArray();
        return new(review.Parent, results.All(clause => clause.State == "COVERED"), results);
    }

    public Task<EngineeringRequirementDiffV1> DiffAsync(Guid id, string before, string after, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("REQUIREMENT_IDENTITY_INVALID", 400);
        var first = Revision(before);
        var last = Revision(after);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRequirement, "query", false, async (access, token) =>
        {
            var store = new EngineeringRequirementStoreV1(db);
            await store.Fence(false, token);
            await access.RequireEngineeringRequirement(id, token);
            var left = await Observe(access, store, await store.Read(id, first, token), token);
            var right = await Observe(access, store, await store.Read(id, last, token), token);
            var changes = new List<EngineeringWorkingChangeV1>();
            void Compare(string path, object? x, object? y)
            {
                JsonElement? from = x==null ? null : JsonSerializer.SerializeToElement(x, EngineeringProductStoreV1.Json);
                JsonElement? to = y==null ? null : JsonSerializer.SerializeToElement(y, EngineeringProductStoreV1.Json);
                if (from?.GetRawText()!=to?.GetRawText()) changes.Add(new(path, from, to));
            }
            Compare("name", left.Body.Name, right.Body.Name);
            if (left.Body.Source is { } leftSource && right.Body.Source is { } rightSource)
            {
                Compare("origin", leftSource.Origin, rightSource.Origin);
                Compare("sourceDescription", leftSource.OriginDescription, rightSource.OriginDescription);
                Compare("sourceLabel", leftSource.SourceLabel, rightSource.SourceLabel);
                Compare("acquisitionBasis", leftSource.AcquisitionBasis, rightSource.AcquisitionBasis);
                Compare("sourceKind", leftSource.Kind, rightSource.Kind);
                foreach (var clause in leftSource.Clauses.Select(x=>x.Id).Union(rightSource.Clauses.Select(x=>x.Id)).Order(StringComparer.Ordinal))
                    Compare("clauses/"+clause, leftSource.Clauses.SingleOrDefault(x=>x.Id==clause), rightSource.Clauses.SingleOrDefault(x=>x.Id==clause));
            }
            if (left.Body.Requirement is { } leftRequirement && right.Body.Requirement is { } rightRequirement)
            {
                Compare("source", leftRequirement.Source, rightRequirement.Source);
                Compare("clauseIds", leftRequirement.ClauseIds, rightRequirement.ClauseIds);
                Compare("interpretation", leftRequirement.Interpretation, rightRequirement.Interpretation);
                Compare("applicableObject", leftRequirement.ApplicableObject, rightRequirement.ApplicableObject);
                Compare("necessity", leftRequirement.Necessity, rightRequirement.Necessity);
                Compare("questions", leftRequirement.ClarificationQuestions, rightRequirement.ClarificationQuestions);
                Compare("clarifications", leftRequirement.Clarifications, rightRequirement.Clarifications);
                Compare("criteria", leftRequirement.Criteria, rightRequirement.Criteria);
                Compare("relations", leftRequirement.Relations, rightRequirement.Relations);
                Compare("decomposition", leftRequirement.Decomposition, rightRequirement.Decomposition);
            }
            return new EngineeringRequirementDiffV1(left.Reference, right.Reference, changes.ToArray());
        }, ct);
    }
}
