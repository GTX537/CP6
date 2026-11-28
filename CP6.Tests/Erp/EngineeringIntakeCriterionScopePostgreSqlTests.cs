using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.Services.Plm;
using CP6.Tests.Platform;
using static CP6.Core.Services.Plm.EngineeringIntakeWireV1;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Wp0560_successive_clause_changes_keep_pending_reviews_and_scope_each_new_criterion_review()
    {
        await using var h = await Seed();
        using var f = await PrepareIntakeHarnessAsync(h);
        var initial = f.Build(await f.PrepareAsync());
        var a = Guid.NewGuid().ToString("D");
        var b = Guid.NewGuid().ToString("D");
        var request = Compose(initial, "1", "1.000001", "2.000001", true, false);
        var accepted = await Receive(request);
        var id = Guid.Parse(accepted.Engineering!.EngineeringRequestId);
        var v1 = await f.Native().ReadAsync(id);
        var history = new List<(EngineeringRequirementSnapshotV1 Version, byte[] Source)> { (v1, SourceBytes(Encode(request))) };
        var peer = await CreateRequirement(f.Native(), v1.Body with { Name = "SIMULATED 对照需求" });
        var analysis = v1.Body.Requirement! with
        {
            Interpretation = "按独立工位分别复核", ApplicableObject = "工位 A 和 B", Necessity = "两个工位均为必需",
            Criteria = [Criterion("C_A", [a]), Criterion("C_B", [b]), Criterion("C_AB", [a, b]), Criterion("C_SUM", ["summary"])],
            Relations = [new("OPEN1", "CONFLICT", peer.Reference, "待工程负责人取舍", "适用范围相交", "双方均为必需")]
        };
        await f.Native().CommandAsync(ReviseRequirement(v1, v1.Body with { Requirement = analysis }));
        var current = await f.Native().ReadAsync(id);
        Assert.Equal("2", current.Reference.Version);
        Assert.Null(current.Body.Requirement!.SourceReviews);
        history.Add((current, SourceBytes(Encode(request))));

        await Step("3", "2", "1.000001", "2.000001", true, false, [], []);
        await Step("4", "3", "1.000001", "2.000002", true, false, [b], ["C_B", "C_AB"]);
        await Step("5", "4", "1.000002", "2.000002", true, false, [a], ["C_A", "C_AB"]);
        await Step("6", "5", "1.000002", "2.000002", true, true, [], []);
        await Step("7", "6", "1.000002", "2.000002", false, false, [a], ["C_A", "C_AB"]);
        await Step("8", "7", "1.000002", "2.000003", false, false, [b], ["C_B", "C_AB"]);
        await Step("9", "8", "1.000002", "2.000003", false, false, [], []);

        foreach (var (version, source) in history)
        {
            var read = await f.Native().ReadAsync(id, version.Reference.Version);
            Assert.Equal(version.OriginalJson, read.OriginalJson);
            Assert.Equal(source, (await f.Native().DownloadOriginalAsync(id, version.Reference.Version, null)).Bytes);
            Assert.DoesNotContain("CRITERION_SOURCE_BASIS_REQUIRED:C_A", read.Missing);
        }

        // 工程人员重新绑定 C_AB。C_A 继续承继删除依据，新建的空数组和 null 则确实缺依据。
        var definition = current.Body.Requirement!;
        var rebound = definition.Criteria.Select(c => c.Id == "C_AB" ? c with { SourceClauseIds = ["summary"] } : c);
        definition = definition with { Criteria = [.. rebound, Criterion("C_EMPTY", []), Criterion("C_NULL", null)] };
        await f.Native().CommandAsync(ReviseRequirement(current, current.Body with { Requirement = definition }));
        current = await f.Native().ReadAsync(id);
        Assert.DoesNotContain("CRITERION_SOURCE_BASIS_REQUIRED:C_A", current.Missing);
        Assert.Contains("CRITERION_SOURCE_BASIS_REQUIRED:C_EMPTY", current.Missing);
        Assert.Contains("CRITERION_SOURCE_BASIS_REQUIRED:C_NULL", current.Missing);
        // A 重新出现时，历史依据用于识别影响。工程人员已改绑的 C_AB 继续引用摘要。
        await Step("11", "9", "1.000002", "2.000003", true, false, [a], ["C_A", "C_EMPTY", "C_NULL"]);
        Assert.Empty(current.Body.Requirement!.Criteria.Single(c => c.Id == "C_A").SourceClauseIds!);
        Assert.Equal(new[] { "summary" }, current.Body.Requirement.Criteria.Single(c => c.Id == "C_AB").SourceClauseIds);
        Assert.DoesNotContain("CRITERION_SOURCE_BASIS_REQUIRED:C_A", current.Missing);
        Assert.Contains("CRITERION_SOURCE_BASIS_REQUIRED:C_EMPTY", current.Missing);
        Assert.Contains("CRITERION_SOURCE_BASIS_REQUIRED:C_NULL", current.Missing);

        static RequirementCriterionV1 Criterion(string name, string[]? clauses) =>
            new(name, "QUALITATIVE", name, "指定材料", "独立目视复核", QualitativeBasis: "准确来源", SourceClauseIds: clauses);

        EngineeringIntakeRequestV1 Compose(EngineeringIntakeRequestV1 form, string version, string aq, string bq, bool hasA, bool reverse)
        {
            var requirement = JsonNode.Parse(initial.Source.Requirement.GetRawText())!.AsObject();
            var template = requirement["lines"]![0]!;
            JsonNode Line(string key, string label, string quantity)
            {
                var row = template.DeepClone();
                row["lineId"] = key;
                row["description"] = label;
                row["quantity"] = quantity;
                return row;
            }
            var lines = new JsonArray(Line(b, "SIMULATED 工位 B 条款", bq));
            if (hasA) lines.Insert(reverse ? 1 : 0, Line(a, "SIMULATED 工位 A 条款", aq));
            requirement["lines"] = lines;
            var source = initial.Source with { SourceVersion = version, Requirement = JsonSerializer.SerializeToElement(requirement, WireJson) };
            return form with { Source = source, SourceBinding = form.SourceBinding with
            {
                SourceVersion = version, SourceDigest = SourceDigest(f.Source.SourceKey, source)
            } };
        }

        async Task<EngineeringIntakeResultV1> Receive(EngineeringIntakeRequestV1 input)
        {
            var receipt = await f.Provider().ReceiveAsync(f.Caller, await f.PublishAsync(input), default);
            Assert.Equal(("DURABLY_RECEIVED", "UNKNOWN"), (receipt.State, receipt.BusinessOutcome));
            var result = IntakeResult(await f.OriginalAsync(input));
            Assert.Equal("ACCEPTED", result.Outcome);
            return result;
        }

        async Task Step(string engineeringVersion, string sourceVersion, string aq, string bq, bool hasA, bool reverse,
            string[] changed, string[] affected)
        {
            var prior = current;
            var priorDefinition = prior.Body.Requirement!;
            var next = await f.PrepareAsync(AfterIntake(request, accepted));
            Assert.Equal(new EngineeringIntakeRefV1(id.ToString("D"), prior.Reference.Version), next.Target);
            request = Compose(f.Build(next, sourceVersion), sourceVersion, aq, bq, hasA, reverse);
            accepted = await Receive(request);
            Assert.Equal(new EngineeringIntakeRefV1(id.ToString("D"), engineeringVersion), accepted.Engineering);
            current = await f.Native().ReadAsync(id);
            var actual = current.Body.Requirement!;
            var priorReviews = priorDefinition.SourceReviews ?? [];
            var reviews = actual.SourceReviews ?? [];
            Assert.Equal(Encode(priorReviews), Encode(reviews.Take(priorReviews.Length).ToArray()));
            var added = reviews.Skip(priorReviews.Length).ToArray();
            if (changed.Length == 0) Assert.Empty(added);
            else
            {
                var review = Assert.Single(added);
                Assert.Equal(affected, review.CriterionIds);
                Assert.Equal(changed, review.ChangedClauseIds);
                Assert.Empty(review.ChangedSourceFields);
                Assert.Equal("REVIEW_REQUIRED", review.Disposition);
                Assert.Equal(prior.Reference, review.PreviousRequirement);
                Assert.Equal(priorDefinition.Source, review.PreviousSource);
                Assert.Equal(actual.Source, review.IncomingSource);
                Assert.Equal(new[] { "OPEN1" }, review.RelationIds);
            }
            var expectedCriteria = priorDefinition.Criteria.Select(c => !hasA && c.SourceClauseIds?.Contains(a) == true
                ? c with { SourceClauseIds = c.SourceClauseIds.Where(clause => clause != a).ToArray() } : c).ToArray();
            Assert.Equal(Encode(expectedCriteria), Encode(actual.Criteria));
            Assert.Equal(Encode(analysis.Relations), Encode(actual.Relations));
            Assert.Equal((analysis.Interpretation, analysis.ApplicableObject, analysis.Necessity),
                (actual.Interpretation, actual.ApplicableObject, actual.Necessity));
            Assert.Equal("DRAFT_ONLY", current.State);
            history.Add((current, SourceBytes(Encode(request))));
        }
    }
}
