using System.Net;
using System.Text.Json;
using CP6.Core.Services.Plm;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task EngineeringRebase_TC018_two_engineers_recover_an_occurrence_conflict_and_adopt_the_newer_frozen_baseline()
    {
        await using var h = await Seed();
        var colleague = new Sys_User { UserName = "tc018-engineer", RoleId = 72 };
        h.Db.Sys_Users.Add(colleague);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var settings = await DocumentHostSettings(new ContentStore());
        settings["RequirementEvaluation__EvidenceMode"] = "DEMO";
        settings["QualitySpecification__EvidenceMode"] = "DEMO";
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, settings);
        // 先建立独立会话，避免登录更新所绑定的人员水位。
        var first = await Login(host, h);
        var second = await DocumentReviewerLogin(host, h, colleague.Id);
        const string revisionPath = "/api/engineering-revisions/owner-v1";
        const string baselinePath = "/api/plm-engineering-baselines/owner-v1";

        async Task<HttpResponseMessage> Send(Dictionary<string, string> session, HttpMethod method, string path, object? command = null)
        {
            using var request = OperationsRequest(session, method, path, command);
            return await host.Send(request);
        }
        async Task<T> Read<T>(Dictionary<string, string> session, string path)
        {
            using var response = await Send(session, HttpMethod.Get, path);
            return await ReadData<T>(response);
        }
        async Task<T> Post<T>(Dictionary<string, string> session, string path, object command)
        {
            using var response = await Send(session, HttpMethod.Post, path, command);
            return await ReadData<T>(response);
        }
        static async Task<T> ReadData<T>(HttpResponseMessage response)
        {
            Assert.True(response.StatusCode == HttpStatusCode.OK, await OperationsFailure(response));
            return await DocumentHttpData<T>(response);
        }

        var original = await BaselineSetup(h, additionalProjectMember: colleague.Id);
        var projectMembers = original.Review.Source.Iteration.Project.Body.Project!.Definition.Members;
        Assert.Contains(h.User, projectMembers);
        Assert.Contains(colleague.Id, projectMembers);
        var sourceFiles = original.Review.Source.Files;
        var files = new CP6.Core.Services.Pub.LocalFileStore(settings["Storage__LocalRoot"]);
        foreach (var entry in sourceFiles.Bytes)
            await files.SaveAsync(new MemoryStream(entry.Value), entry.Key);
        var baselines = Baselines(h, sourceFiles);
        var owner = new EngineeringRevisionOwnerV1(h.Db, CandidateAuthority(h), Documents(h, sourceFiles), Specifications(h, sourceFiles), baselines);
        var sourceRevision = await owner.ReadAsync(original.Command.Revision.Id, null);
        var member = sourceRevision.Draft.Components.Single(x => x.Kind == "QUALITY_SPEC");
        var initial = await MakeRevision(owner, sourceRevision.Draft.ProductRef);
        var targetPath = $"{revisionPath}/{initial.Reference.Id}";
        await owner.CommandAsync(PatchRevision(initial, Upsert(member)));

        var aResult = await Post<EngineeringFreezeResultV1>(first, $"{baselinePath}/freeze", original.Command);
        var a = await Read<EngineeringBaselineViewV1>(first, $"{baselinePath}/{aResult.Baseline.Id}");
        Assert.Equal("FROZEN", a.State);
        Assert.Equal(aResult.Baseline, a.Reference);
        var baseRef = BaselineRef(a.Reference);
        var anchor = new EngineeringRebaseSourcesV1(initial.CandidateRef, baseRef);
        var separateDraft = await owner.ReadAsync(initial.Reference.Id, null);
        var anchorPreview = await owner.RebasePreviewAsync(initial.Reference.Id, anchor);
        await owner.CommandAsync(RebaseCommand(separateDraft, anchor, anchorPreview.Changes.Select(change =>
            new EngineeringRebaseResolutionV1(change.Path, change.Path is "configuration" or "goal" ? "ACCEPT_SOURCE" : "KEEP_BASE",
                "从 BL-A 建立组成基准，保留目标修订的成员范围")).ToArray()));
        var basedOnA = await owner.ReadAsync(initial.Reference.Id, null);
        Assert.Equal(baseRef, basedOnA.CompositionBase);
        Assert.Equal(member, Assert.Single(basedOnA.Draft.Components));

        var successor = await baselines.SuccessorAsync(new(Guid.NewGuid(), a.Reference, original.Command.Revision,
            original.Command.ExpectedEtag, "从 BL-A 建立待评审的后继版"));
        var successorDraft = await owner.ReadAsync(sourceRevision.Reference.Id, null);
        Assert.Equal(successor.Revision, successorDraft.Reference);
        Assert.Equal(a.Reference, successor.ParentBaseline);
        const string incomingValue = "BL-B 质检位置备注";
        await owner.CommandAsync(PatchRevision(successorDraft, Upsert(member with { Note = incomingValue })));
        var newer = await owner.ReadAsync(sourceRevision.Reference.Id, null);
        var bCommand = await PrepareTc018NewerBaseline(h, original, newer);
        var bResult = await Post<EngineeringFreezeResultV1>(first, $"{baselinePath}/freeze", bCommand);
        var b = await Read<EngineeringBaselineViewV1>(second, $"{baselinePath}/{bResult.Baseline.Id}");
        Assert.Equal("FROZEN", b.State);
        Assert.Equal(bResult.Baseline, b.Reference);
        Assert.NotEqual(a.Reference, b.Reference);
        Assert.Equal(a.Original.Revision.Reference.Id, b.Original.Revision.Reference.Id);
        Assert.Equal(a.Original.Revision.Draft.ProductRef, b.Original.Revision.Draft.ProductRef);
        Assert.Equal(successor.Revision.Version, b.Original.Revision.ParentWorkingVersion);
        Assert.Equal(incomingValue, b.Original.Revision.Draft.Components.Single(x => x.OccurrenceId == member.OccurrenceId).Note);
        Assert.Equal(2, await BaselineCount(h, "baselines"));
        var sources = new EngineeringRebaseSourcesV1(baseRef, BaselineRef(b.Reference));
        var firstScope = await Read<EngineeringRevisionScopeV1>(first, $"{revisionPath}/scope");
        var secondScope = await Read<EngineeringRevisionScopeV1>(second, $"{revisionPath}/scope");
        Assert.Equal(new EngineeringRevisionScopeV1(h.Tenant, h.User), firstScope);
        Assert.Equal(new EngineeringRevisionScopeV1(h.Tenant, colleague.Id), secondScope);
        Assert.NotEqual(firstScope.ActorId, secondScope.ActorId);

        var expected = await Read<EngineeringRevisionSnapshotV1>(first, targetPath);
        Assert.Equal(basedOnA.Reference, expected.Reference);
        Assert.Equal(baseRef, expected.CompositionBase);
        var notePath = $"components/{member.OccurrenceId}/note";
        var preview = await Post<EngineeringRebasePreviewV1>(second, $"{targetPath}/rebase-preview", sources);
        Assert.Equal(expected.Reference, preview.Revision);
        Assert.Equal(expected.Etag, preview.Etag);
        var change = Assert.Single(preview.Changes, x => x.Path == notePath);
        Assert.Equal(member.Note, change.BaseValue.GetString());
        Assert.Equal(incomingValue, change.IncomingValue.GetString());
        EngineeringRebaseResolutionV1[] Decisions(EngineeringRebasePreviewV1 value, string reason) => value.Changes.Select(x => x.Path == notePath
            ? new EngineeringRebaseResolutionV1(x.Path, "ACCEPT_SOURCE", reason)
            : new EngineeringRebaseResolutionV1(x.Path, "MANUAL", "沿用目标修订范围内的原稿值", x.CurrentValue)).ToArray();

        const string winnerValue = "甲先保存的质检位置备注";
        const string rejectedReason = "乙按已审 BL-B 采用新版质检备注";
        var accepted = PatchRevision(expected, Upsert(member with { Note = winnerValue }));
        var rejected = RebaseCommand(expected, sources, Decisions(preview, rejectedReason));
        var saved = await Post<EngineeringRevisionResultV1>(first, $"{revisionPath}/commands", accepted);
        var winner = await Read<EngineeringRevisionSnapshotV1>(first, targetPath);
        Assert.Equal(accepted.OperationId, saved.OperationId);
        Assert.Equal(winner.Reference.Digest, saved.Digest);
        Assert.Equal(baseRef, winner.CompositionBase);
        using var stale = await Send(second, HttpMethod.Post, $"{revisionPath}/commands", rejected);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        var staleBody = await OperationsFailure(stale);
        Assert.Contains("ENGINEERING_WORKING_VERSION_CHANGED", staleBody);

        var operationPath = $"{revisionPath}/operations/{rejected.OperationId}";
        var conflictPath = $"{operationPath}/conflict";
        using var read = await Send(second, HttpMethod.Get, conflictPath);
        Assert.True(read.Headers.CacheControl?.NoStore);
        var conflict = await ReadData<EngineeringRevisionConflictV1>(read);
        var rawConflict = await read.Content.ReadAsStringAsync();
        Assert.Equal(rejected.OperationId, conflict.OperationId);
        Assert.Equal(rejected.Reason, conflict.Reason);
        Assert.Equal(sources, conflict.Sources);
        Assert.Equal(expected.CandidateRef, conflict.Expected.Candidate);
        Assert.Equal(winner.CandidateRef, conflict.Current.Candidate);
        var item = Assert.Single(conflict.Items);
        Assert.Equal(member.OccurrenceId, item.OccurrenceId);
        Assert.Equal(notePath, item.Path);
        Assert.True(item.Conflict);
        Assert.Equal(member.Note, item.ExpectedValue.GetString());
        Assert.Equal(winnerValue, item.CurrentValue.GetString());
        Assert.Equal(incomingValue, item.AttemptedValue.GetString());
        Assert.Equal(member.SourceRef, item.ExpectedSource);
        Assert.Equal(member.SourceRef, item.CurrentSource);
        Assert.Equal(member.SourceRef, item.AttemptedSource);
        Assert.Equal(rejectedReason, item.Reason);

        using var retry = await Send(second, HttpMethod.Post, $"{revisionPath}/commands", rejected);
        Assert.Equal(HttpStatusCode.PreconditionFailed, retry.StatusCode);
        using var otherActor = await Send(first, HttpMethod.Get, conflictPath);
        Assert.Equal(HttpStatusCode.Forbidden, otherActor.StatusCode);
        var status = await Read<EngineeringRevisionOperationV1>(second, operationPath);
        Assert.Equal(new EngineeringRevisionOperationV1(rejected.OperationId, "CONFLICT"), status);
        Assert.Equal(saved, (await Read<EngineeringRevisionOperationV1>(first, $"{revisionPath}/operations/{accepted.OperationId}")).Result);
        Assert.Equal(winner.Reference, (await owner.ReadAsync(expected.Reference.Id, null)).Reference);
        Assert.Equal(EngineeringProductValuesV1.Revision(expected.Reference.Version) + 1, EngineeringProductValuesV1.Revision(winner.Reference.Version));
        var appendedActors = await h.Db.Database.SqlQuery<Guid>($"""
            SELECT actor_id AS "Value" FROM cp6_engineering_revision_v1.working_versions
            WHERE tenant_id={h.Tenant} AND revision_id={expected.Reference.Id} AND revision>{EngineeringProductValuesV1.Revision(expected.Reference.Version)}
            """).ToArrayAsync();
        Assert.Equal(firstScope.ActorId, Assert.Single(appendedActors));
        var successfulKeys = await h.Db.Database.SqlQuery<Guid>($"""
            SELECT operation_id AS "Value" FROM cp6_main_owner_v1.operations
            WHERE tenant_id={h.Tenant} AND owner={EngineeringRevisionValuesV1.RevisionOwner} AND operation_id IN ({accepted.OperationId},{rejected.OperationId})
            """).ToArrayAsync();
        Assert.Equal(accepted.OperationId, Assert.Single(successfulKeys));
        var storedOriginal = await h.Db.Database.SqlQuery<string>($"""
            SELECT convert_from(body,'UTF8') AS "Value" FROM cp6_engineering_revision_v1.revision_conflicts
            WHERE tenant_id={h.Tenant} AND operation_id={rejected.OperationId} AND actor_id={secondScope.ActorId}
              AND body_digest=encode(sha256(body),'hex') AND input_digest=encode(sha256(input_bytes),'hex')
            """).SingleAsync();
        Assert.Equal(JsonSerializer.Serialize(conflict, EngineeringProductStoreV1.Json), storedOriginal);

        var triggerErrors = new Dictionary<string, string>();
        foreach (var sql in new[]
        {
            "UPDATE cp6_engineering_revision_v1.revision_conflicts SET body=body",
            "DELETE FROM cp6_engineering_revision_v1.revision_conflicts",
            "TRUNCATE cp6_engineering_revision_v1.revision_conflicts"
        })
        {
            await using var tx = await h.Db.Database.BeginTransactionAsync();
            var error = await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlRawAsync(sql));
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
            Assert.Equal("ENGINEERING_WORKING_ORIGINAL_IMMUTABLE", error.MessageText);
            await tx.RollbackAsync();
            triggerErrors.Add(sql, error.SqlState);
        }

        var afterPreview = await Post<EngineeringRebasePreviewV1>(second, $"{targetPath}/rebase-preview", sources);
        Assert.Equal(winner.Reference, afterPreview.Revision);
        var confirmedChange = Assert.Single(afterPreview.Changes, x => x.Path == notePath);
        Assert.True(confirmedChange.Conflict);
        Assert.Equal(winnerValue, confirmedChange.CurrentValue.GetString());
        var resolution = RebaseCommand(winner, sources, Decisions(afterPreview, "核对甲的修改后显式采用 BL-B 的质检备注"));
        var completed = await Post<EngineeringRevisionResultV1>(second, $"{revisionPath}/commands", resolution);
        var latest = await Read<EngineeringRevisionSnapshotV1>(second, targetPath);
        Assert.Equal(completed.Digest, latest.Reference.Digest);
        Assert.Equal(sources.Incoming, latest.CompositionBase);
        Assert.Equal(winner.Reference.Version, latest.ParentWorkingVersion);
        Assert.Equal(incomingValue, latest.Draft.Components.Single(x => x.OccurrenceId == member.OccurrenceId).Note);
        Assert.Equal(resolution.OperationId, latest.Rebase!.OperationId);
        Assert.Equal(winner.CandidateRef, latest.Rebase.PreviousCandidate);
        Assert.Equal(sources, latest.Rebase.Input.Sources);
        Assert.Equal(resolution.Rebase!.Resolutions.Select(x => (x.Path, x.Decision, x.Reason)),
            latest.Rebase.Input.Resolutions.Select(x => (x.Path, x.Decision, x.Reason)));
        Assert.Equal(expected.OriginalJson, (await owner.ReadAsync(expected.Reference.Id, expected.Reference.Version)).OriginalJson);
        Assert.Equal(winner.OriginalJson, (await owner.ReadAsync(winner.Reference.Id, winner.Reference.Version)).OriginalJson);
        Assert.Equal(a.OriginalJson, (await baselines.ReadAsync(a.Reference.Id)).OriginalJson);
        Assert.Equal(b.OriginalJson, (await baselines.ReadAsync(b.Reference.Id)).OriginalJson);
        using var recovered = await Send(second, HttpMethod.Get, conflictPath);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal(rawConflict, await recovered.Content.ReadAsStringAsync());

        if (Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT") is { } evidenceRoot)
            await File.WriteAllTextAsync(Path.Combine(evidenceRoot, "tc-018.json"), JsonSerializer.Serialize(new
            {
                baselineA = new { a.Reference, a.State, working = a.Original.Revision.Reference, command = original.Command, receipt = aResult },
                baselineB = new { b.Reference, b.State, working = b.Original.Revision.Reference, command = bCommand, receipt = bResult },
                projectMembers, httpFixture = settings,
                successor, firstScope, secondScope, expected = expected.CandidateRef, compositionBase = expected.CompositionBase,
                accepted, saved, rejected, staleStatus = (int)stale.StatusCode, staleBody, retryStatus = (int)retry.StatusCode,
                conflictPath, conflict, storedOriginal, status, appended = appendedActors.Length, successfulKeys, triggerErrors,
                resolution, completed, finalBasis = latest.CompositionBase, finalDecision = latest.Rebase
            }, new JsonSerializerOptions(EngineeringProductStoreV1.Json) { WriteIndented = true }));
    }

    private async Task<EngineeringFreezeCommandV1> PrepareTc018NewerBaseline(Harness h, BaselineFixture previous,
        EngineeringRevisionSnapshotV1 newer)
    {
        var source = previous.Review.Source;
        var checks = new EngineeringSourceCheckService(h.Db, CandidateAuthority(h), Documents(h, source.Files), Specifications(h, source.Files));
        var checkedResult = await checks.CheckAsync(source.Check.Original.Command with
        {
            OperationId = Guid.NewGuid(), Revision = newer.Reference, Candidate = newer.CandidateRef,
            ExpectedEtag = newer.Etag, Reason = "对 BL-A 的后继候选执行新来源检查"
        });
        var check = await checks.ReadAsync(checkedResult.CheckRunId);
        Assert.Equal("PASS", check.Original.State);
        var previousWork = source.Iteration.Iteration.Body.Iteration!;
        var iterations = Iterations(h, source.Files);
        var work = await IterationWrite(iterations, new EngineeringIterationCommandV1
        {
            OperationId = Guid.NewGuid(), ObjectId = Guid.NewGuid(), Action = "CREATE_ITERATION", ProjectId = previousWork.ProjectId,
            Plan = previousWork.Plan with { Name = "BL-B 后继技术方案" }, Origin = previousWork.Origin,
            RequirementSet = source.Iteration.Set.Set.Source.Set.Reference, Reason = "由同一项目复核后继候选"
        });
        work = await IterationWrite(iterations, IterationChange(work, "SAVE_TASK") with { Task = IterationTask(h, source.Iteration.Reviewer) });
        work = await IterationWrite(iterations, IterationChange(work, "START"));
        var checkAtAdoption = await checks.ReadAsync(check.Reference.Id);
        Assert.True(checkAtAdoption.Freshness == "CURRENT",
            $"后继 CheckRun 水位差异 {string.Join('、', checkAtAdoption.Changes)}");
        var candidate = source.Candidate with
        {
            OperationId = Guid.NewGuid(), CandidateId = Guid.NewGuid(), IterationId = work.Reference.Id,
            ExpectedIteration = work.Reference, ExpectedIterationEtag = work.Etag, SourceCheck = check.Reference, Title = "BL-B 准确后继方案"
        };
        await source.Candidates.CommandAsync(candidate);
        var reviews = IterationReviews(h, source.Files);
        var context = await reviews.ContextAsync(work.Reference.Id);
        await reviews.PolicyAsync(work.Reference.Id, context.Etag, source.Context.Policies.Last().Fact.Body with
        {
            OperationId = Guid.NewGuid(), Reason = "沿用独立专业评审要求"
        });
        var decision = await SubmitIterationDecision(h, source with
        {
            Iteration = source.Iteration with { Iteration = work }, Check = check, Candidate = candidate, Context = context
        }, previous.Review.Service, Authoring(h), BindingTime().AddSeconds(1));
        return (await CompleteBaselineReview(h, decision, apply: true)).Command;
    }
}
