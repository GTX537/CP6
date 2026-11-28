using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Plm;
using CP6.Tests.Platform;
using static CP6.Core.Services.Plm.EngineeringIntakeWireV1;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Wp0560_successor_preserves_native_facts_and_marks_only_affected_basis_against_adopted_original()
    {
        await using var h = await Seed();
        using var f = await PrepareIntakeHarnessAsync(h);
        EngineeringRequirementService Native() => f.Native();
        var initial = f.Build(await f.PrepareAsync());
        var first = await Receive(initial);
        var id = Guid.Parse(first.Engineering!.EngineeringRequestId);
        var v1 = await Native().ReadAsync(id);
        var lineId = initial.Source.Requirement.GetProperty("lines")[0].GetProperty("lineId").GetString()!;
        var peer = await CreateRequirement(Native(), v1.Body with { Name = "SIMULATED 对照需求" });
        var c1 = new RequirementCriterionV1("C1", "QUALITATIVE", "指定工位", "相同材料", "独立目视复核",
            QualitativeBasis: "原规范第 1 条", SourceClauseIds: ["summary"]);
        var c2 = c1 with { Id = "C2", QualitativeBasis = "原数量条款", SourceClauseIds = [lineId] };
        var open = new RequirementRelationV1("OPEN1", "CONFLICT", peer.Reference, "仍待工程负责人判定", "适用范围重叠", "双方均为必需");
        var definition = v1.Body.Requirement! with
        {
            Interpretation = "已登记的 Main 工程解释", ApplicableObject = "指定工位与产品", Necessity = "必须",
            ClarificationQuestions = [.. v1.Body.Requirement!.ClarificationQuestions, "Main 增补问题"],
            Criteria = [c1, c2], Relations = [open]
        };
        await Native().CommandAsync(ReviseRequirement(v1, v1.Body with { Requirement = definition }));
        var v2 = await Native().ReadAsync(id);
        Assert.Equal("2", v2.Reference.Version);

        var versionOnly = await Successor(initial, first, "2", initial.Source.Requirement);
        var second = await Receive(versionOnly);
        Assert.Equal(new EngineeringIntakeRefV1(id.ToString("D"), "3"), second.Engineering);
        var v3 = await Native().ReadAsync(id);
        Assert.Equal(Encode(definition with { Source = v3.Body.Requirement!.Source }), Encode(v3.Body.Requirement));
        Assert.NotEqual(definition.Source, v3.Body.Requirement.Source);
        Assert.Null(v3.Body.Requirement.SourceReviews);
        Assert.Equal(v2.OriginalJson, (await Native().ReadAsync(id, "2")).OriginalJson);
        Assert.Equal(SourceBytes(Encode(initial)), (await Native().DownloadOriginalAsync(id, "2", null)).Bytes);
        Assert.Equal(SourceBytes(Encode(versionOnly)), (await Native().DownloadOriginalAsync(id, "3", null)).Bytes);

        var changed = JsonNode.Parse(initial.Source.Requirement.GetRawText())!.AsObject();
        changed["summary"] = "更新后的客户摘要";
        var amended = JsonSerializer.SerializeToElement(changed, WireJson);
        await f.ControlAsync(needsInfo: true);
        var attempt = await Successor(versionOnly, second, "3", amended);
        var needsInfo = await Receive(attempt);
        Assert.Equal("NEEDS_INFO", needsInfo.Outcome);
        Assert.Equal(second.Engineering, needsInfo.Engineering);
        await f.ControlAsync();

        var acceptedChange = await Successor(attempt, needsInfo, "4", amended);
        var fourth = await Receive(acceptedChange);
        Assert.Equal(new EngineeringIntakeRefV1(id.ToString("D"), "4"), fourth.Engineering);
        var v4 = await Native().ReadAsync(id);
        Assert.Equal(definition.Interpretation, v4.Body.Requirement!.Interpretation);
        Assert.Equal(definition.ApplicableObject, v4.Body.Requirement.ApplicableObject);
        Assert.Equal(definition.Necessity, v4.Body.Requirement.Necessity);
        Assert.Equal(Encode(definition.Criteria), Encode(v4.Body.Requirement.Criteria));
        Assert.Equal(open, Assert.Single(v4.Body.Requirement.Relations));
        Assert.Contains("REQUIREMENT_SOURCE_REEVALUATION_REQUIRED", v4.Missing);
        var review = Assert.Single(v4.Body.Requirement.SourceReviews!);
        Assert.Equal(v3.Reference, review.PreviousRequirement);
        Assert.Equal(v3.Body.Requirement.Source, review.PreviousSource);
        Assert.Equal(v4.Body.Requirement.Source, review.IncomingSource);
        Assert.Equal("REVIEW_REQUIRED", review.Disposition);
        Assert.Equal(new[] { "summary" }, review.ChangedClauseIds);
        Assert.Empty(review.ChangedSourceFields);
        Assert.Equal(new[] { "interpretation", "applicableObject", "necessity" }, review.Fields);
        Assert.Equal(new[] { "C1" }, review.CriterionIds);
        Assert.Equal(new[] { "OPEN1" }, review.RelationIds);
        Assert.Contains((await Native().DiffAsync(id, "3", "4")).Changes, change => change.Path == "sourceReviews");

        // 保留原行描述，精确数量变化仍会影响该条款的判据。
        changed["lines"]![0]!["quantity"] = "12.340062";
        var quantity = await Successor(acceptedChange, fourth, "5", JsonSerializer.SerializeToElement(changed, WireJson));
        var fifth = await Receive(quantity);
        var v5 = await Native().ReadAsync(id);
        var quantityReview = v5.Body.Requirement!.SourceReviews![^1];
        Assert.Equal(new[] { lineId }, quantityReview.ChangedClauseIds);
        Assert.Equal(new[] { "C2" }, quantityReview.CriterionIds);
        Assert.Equal(Encode(c2), Encode(v5.Body.Requirement.Criteria.Single(c => c.Id == "C2")));

        changed["lines"]!.AsArray().Clear();
        var removedLine = JsonSerializer.SerializeToElement(changed, WireJson);
        var removal = await Successor(quantity, fifth, "6", removedLine);
        var sixth = await Receive(removal);
        var v6 = await Native().ReadAsync(id);
        Assert.Equal("6", v6.Reference.Version);
        Assert.Equal(new[] { "summary" }, v6.Body.Requirement!.ClauseIds);
        Assert.Equal(Encode(c2 with { SourceClauseIds = [] }), Encode(v6.Body.Requirement.Criteria.Single(c => c.Id == "C2")));
        Assert.Equal(Encode(c2), Encode((await Native().ReadAsync(id, "5")).Body.Requirement!.Criteria.Single(c => c.Id == "C2")));
        var removalReview = v6.Body.Requirement.SourceReviews![^1];
        Assert.Equal(v5.Reference, removalReview.PreviousRequirement);
        Assert.Equal(new[] { lineId }, removalReview.ChangedClauseIds);
        Assert.Equal(new[] { "C2" }, removalReview.CriterionIds);

        var nextVersionOnly = await Successor(removal, sixth, "7", removedLine);
        await Receive(nextVersionOnly);
        var v7 = await Native().ReadAsync(id);
        Assert.Equal("7", v7.Reference.Version);
        Assert.Equal(Encode(v6.Body.Requirement.SourceReviews), Encode(v7.Body.Requirement!.SourceReviews));
        Assert.Equal(v4.OriginalJson, (await Native().ReadAsync(id, "4")).OriginalJson);
        Assert.Equal(SourceBytes(Encode(acceptedChange)), (await Native().DownloadOriginalAsync(id, "4", null)).Bytes);
        Assert.Equal(SourceBytes(Encode(nextVersionOnly)), (await Native().DownloadOriginalAsync(id, "7", null)).Bytes);

        var forged = ReviseRequirement(v7, v7.Body with { Requirement = v7.Body.Requirement with
        {
            SourceReviews = [review with { Disposition = "ACCEPTED" }]
        } });
        await ExpectIntakeFailure("REQUIREMENT_SOURCE_REVIEW_OWNER_REQUIRED", () => Native().CommandAsync(forged));
        await Native().CommandAsync(ReviseRequirement(v7, v7.Body with { Requirement = v7.Body.Requirement with { SourceReviews = null } }));
        var v8 = await Native().ReadAsync(id);
        Assert.Equal(Encode(v7.Body.Requirement.SourceReviews), Encode(v8.Body.Requirement!.SourceReviews));
        Assert.Contains("REQUIREMENT_SOURCE_REEVALUATION_REQUIRED", v8.Missing);

        async Task<EngineeringIntakeRequestV1> Successor(EngineeringIntakeRequestV1 previous, EngineeringIntakeResultV1 result,
            string sourceVersion, JsonElement requirement)
        {
            var prepared = f.Build(await f.PrepareAsync(AfterIntake(previous, result)), sourceVersion);
            var source = initial.Source with { SourceVersion = sourceVersion, Requirement = requirement };
            return prepared with
            {
                Source = source,
                SourceBinding = prepared.SourceBinding with { SourceDigest = SourceDigest(f.Source.SourceKey, source) }
            };
        }

        async Task<EngineeringIntakeResultV1> Receive(EngineeringIntakeRequestV1 request)
        {
            var receipt = await f.Provider().ReceiveAsync(f.Caller, await f.PublishAsync(request), default);
            Assert.Equal("UNKNOWN", receipt.BusinessOutcome);
            return IntakeResult(await f.OriginalAsync(request));
        }

    }

    [CurrentPermissionPostgresFact]
    public async Task Wp0560_opaque_reference_keys_parse_verbatim_and_unknown_Targets_never_write()
    {
        await using var h = await Seed();
        using var f = await PrepareIntakeHarnessAsync(h);
        var initial = f.Build(await f.PrepareAsync());
        await f.Provider().ReceiveAsync(f.Caller, await f.PublishAsync(initial), default);
        var first = IntakeResult(await f.OriginalAsync(initial));
        var next = await f.PrepareAsync(AfterIntake(initial, first));
        var known = next.Target!;
        var opaque = new EngineeringIntakeRefV1("ER-HO-900", "req-r1");
        var wide = string.Concat(Enumerable.Repeat("𠮷", 128));
        var sample = f.Build(next, "2");
        foreach (var key in new[] { known, opaque with { EngineeringRevision = known.EngineeringRevision },
            opaque with { EngineeringRequestId = known.EngineeringRequestId }, opaque, new EngineeringIntakeRefV1(wide, new string('r', 128)),
            new EngineeringIntakeRefV1("E", "1"),
            new EngineeringIntakeRefV1(" er-HO-900 ", " Req-R1 ") })
        {
            var parsed = Read<EngineeringIntakeRequestV1>(Encode(sample with { Target = key, NextAuthorization = next with { Target = key } }),
                EngineeringIntakeWireV1.Request);
            Assert.Equal(key, parsed.Target);
            Assert.Equal(key, parsed.NextAuthorization.Target);
        }

        foreach (var invalid in new[] { "", "   ", new string('x', 129), "req\u0000r1", "req\u0085r1" })
        {
            foreach (var key in new[] { opaque with { EngineeringRequestId = invalid }, opaque with { EngineeringRevision = invalid } })
            {
                AssertBad(sample with { Target = key });
                AssertBad(sample with { NextAuthorization = next with { Target = key } });
            }
        }

        var before = await f.CountsAsync();
        foreach (var (target, authorizationTarget) in new[] { (opaque, opaque), (known, opaque) })
        {
            var request = f.Build(next, "2") with { Target = target, NextAuthorization = next with { Target = authorizationTarget } };
            var raw = await f.PublishAsync(request);
            var error = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => f.Provider().ReceiveAsync(f.Caller, raw, default));
            Assert.Equal((409, "STALE_NEXT_CONTEXT"), (error.Status, error.Message));
            Assert.Equal(before, await f.CountsAsync());
        }
        Assert.Equal(known, (await f.PrepareAsync(AfterIntake(initial, first))).Target);

        static void AssertBad(EngineeringIntakeRequestV1 request)
        {
            var error = Assert.Throws<OrderPriceCorrectionException>(() => Read<EngineeringIntakeRequestV1>(Encode(request),
                EngineeringIntakeWireV1.Request));
            Assert.Equal((400, "ENGINEERING_INTAKE_CONTRACT_INVALID"), (error.Status, error.Message));
        }
    }
}
