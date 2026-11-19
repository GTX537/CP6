using System.Net;
using System.Text.Json;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Gl_owner_rejected_outbox_rolls_back_and_exact_original_survives_current_write_revocation()
    {
        await using var h = await Seed();
        var activated = await SeedGlBoard(h, "recovery");
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, GlHostSettings());
        var maker = new GlClient(host, await Login(host, h));
        var checker = new GlClient(host, await Login(host, "gl-checker-recovery", "Price1!Password", h.Tenant.ToString("N")));
        await maker.AuthenticateBook();
        await checker.AuthenticateBook();
        var root = await GlCreate(maker, "SIM-OUTBOX", GlContent("模拟事务提交", GlBusinessDay), true);
        var head = await GlApproved(maker, checker, root);
        var view = await GlPreview(checker, root, head);
        var preview = view.GetProperty("previewId").GetGuid();
        var key = Guid.NewGuid();
        var publish = GlInput(head, "publish", root, previewId: preview, operation: key);
        var notObserved = await checker.Read($"/commands/{key:D}");
        Assert.Equal("NOT_OBSERVED", notObserved.GetProperty("kind").GetString());
        Assert.False(notObserved.GetProperty("provesPermanentNoEffect").GetBoolean());
        Assert.True(notObserved.GetProperty("mayRetryOriginal").GetBoolean());

        await h.Db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION cp6_fin_gl_v1.gl_test_reject_notification() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'SIMULATED_GL_OUTBOX_FAILURE' USING ERRCODE='23514';
            END $$;
            CREATE TRIGGER gl_test_reject_notification BEFORE INSERT ON cp6_fin_gl_v1.outbox
                FOR EACH ROW EXECUTE FUNCTION cp6_fin_gl_v1.gl_test_reject_notification();
            """);
        try
        {
            await GlReject(checker, "/commands", publish, HttpStatusCode.ServiceUnavailable, "GL_STORE_UNAVAILABLE");
            var rolledBack = await checker.Head(root);
            Assert.Equal(head.GetProperty("heads").GetRawText(), rolledBack.GetProperty("heads").GetRawText());
            Assert.Equal(head.GetProperty("scopeRevision").GetInt64(), rolledBack.GetProperty("scopeRevision").GetInt64());
            Assert.Equal("APPROVED", rolledBack.GetProperty("proposal").GetProperty("state").GetString());
            Assert.Equal(JsonValueKind.Null, rolledBack.GetProperty("timeline").ValueKind);
            Assert.Equal("NOT_OBSERVED", (await checker.Read($"/commands/{key:D}")).GetProperty("kind").GetString());
            Assert.Empty(await h.Db.Database.SqlQuery<Guid>($"""
                SELECT operation_ref AS "Value" FROM cp6_fin_gl_v1.consumed_previews
                WHERE (tenant_id,ledger_id,id)=({h.Tenant},{activated.Ledger},{preview})
                """).ToArrayAsync());
        }
        finally
        {
            await h.Db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER gl_test_reject_notification ON cp6_fin_gl_v1.outbox;
                DROP FUNCTION cp6_fin_gl_v1.gl_test_reject_notification();
                """);
        }
        var original = await checker.Write("/commands", publish);
        Assert.Equal("RELEASED", original.GetProperty("kind").GetString());
        Assert.Equal(original.GetRawText(), (await checker.Write("/commands", publish)).GetRawText());
        await GlReject(checker, "/commands", GlInput(head, "suspend", root, operation: key),
            HttpStatusCode.Conflict, "OPERATION_INPUT_CONFLICT");

        var menu = await h.Db.Sys_Menus.SingleAsync(item => item.MenuKey == "fin-account");
        await h.Db.Sys_RoleActions.Where(item => item.RoleId == 72 && item.MenuId == menu.MenuId &&
            item.ActionCode == "release").ExecuteDeleteAsync();
        var readOnly = await checker.Read($"/commands/{key:D}");
        Assert.Equal(original.GetRawText(), readOnly.GetRawText());

        var hidden = new CP6.Entity.DomainModels.Sys.Sys_RoleFieldPerm
            { RoleId = 72, ResourceKey = "fin-account", FieldName = "RequirePartner", Access = 3 };
        h.Db.Sys_RoleFieldPerms.Add(hidden);
        await h.Db.SaveChangesAsync();
        using (var withheld = await checker.Send(HttpMethod.Get, $"/commands/{key:D}"))
        {
            var refusal = await GlJson(withheld, HttpStatusCode.Forbidden);
            Assert.Equal("RECEIPT_WITHHELD", refusal.GetProperty("message").GetString());
            Assert.DoesNotContain(original.GetProperty("versionId").GetGuid().ToString(), refusal.GetRawText());
        }
        using (var noHistory = await checker.Send(HttpMethod.Get,
            $"/roots/{root:D}/history?proposalId={original.GetProperty("proposalId").GetGuid():D}&revision=1"))
        {
            Assert.Equal("WITHHELD", (await GlJson(noHistory, HttpStatusCode.Forbidden)).GetProperty("message").GetString());
        }

        h.Db.Sys_RoleFieldPerms.Remove(hidden);
        await h.Db.SaveChangesAsync();
        await h.Db.Sys_RoleActions.Where(item => item.RoleId == 72 && item.MenuId == menu.MenuId &&
            item.ActionCode == "history").ExecuteDeleteAsync();
        using (var historyRevoked = await checker.Send(HttpMethod.Get, $"/commands/{key:D}"))
            Assert.Equal("RECEIPT_WITHHELD", (await GlJson(historyRevoked, HttpStatusCode.Forbidden)).GetProperty("message").GetString());

        var outbox = await h.Db.Database.SqlQuery<string>($"""
            SELECT o.body AS "Value" FROM cp6_fin_gl_v1.outbox o
            JOIN cp6_fin_gl_v1.events e ON (e.tenant_id,e.ledger_id,e.operation_ref)=(o.tenant_id,o.ledger_id,o.event_id)
            JOIN cp6_main_owner_v1.operations c ON c.tenant_id=o.tenant_id AND c.operation_id=o.event_id
            WHERE (o.tenant_id,o.ledger_id,o.event_id)=({h.Tenant},{activated.Ledger},{key}) AND o.state='PENDING'
              AND e.body::jsonb->'audit'->>'source'='FIN_GL_OWNER_UTC'
            """).SingleAsync();
        Assert.DoesNotContain("模拟事务提交", outbox);
        Assert.DoesNotContain("原", outbox);
        Assert.Contains(root.ToString(), outbox);

        await DeviceEvidence("wp11-01-gl-atomic-receipt.json", new
        {
            root, key, rollback = 503, atomicHeadsUnchanged = true, exactRetrySuccessful = true,
            differentInput = 409, afterReleaseRevocation = 200, historicalFieldWithheld = 403, historicalActionWithheld = 403, outboxEnqueuedOnce = true
        });
    }

    [CurrentPermissionPostgresFact]
    public async Task Gl_owner_freezes_submitted_content_and_only_one_successor_or_new_timeline_can_win()
    {
        await using var h = await Seed();
        var actual = await SeedGlBoard(h, "revision");
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, GlHostSettings());
        await using var replica = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, GlHostSettings());
        var maker = new GlClient(host, await Login(host, h));
        var secondMaker = new GlClient(replica, await Login(replica, h));
        var checker = new GlClient(replica, await Login(replica, "gl-checker-revision", "Price1!Password", h.Tenant.ToString("N")));
        await maker.AuthenticateBook();
        await secondMaker.AuthenticateBook();
        await checker.AuthenticateBook();
        var day = GlBusinessDay;
        var root = await GlCreate(maker, "SIM-REVISION", GlContent("一版", day), true);
        var first = await maker.Head(root);
        var firstVersion = first.GetProperty("version").GetProperty("id").GetGuid();
        var firstDigest = first.GetProperty("version").GetProperty("digest").GetString();
        var firstProposal = first.GetProperty("proposal").GetProperty("id").GetGuid();
        await GlStep(maker, root, "revise", GlContent("二版", day), true);
        Assert.NotEqual(firstVersion, (await maker.Head(root)).GetProperty("version").GetProperty("id").GetGuid());
        await GlStep(maker, root, "submit");
        var submitted = await checker.Head(root);
        await GlReject(maker, "/commands", GlInput(submitted, "revise", root,
            content: GlContent("提交后的直接改写", day), activate: true), HttpStatusCode.Conflict, "PROPOSAL_STATE_CONFLICT");
        await GlStep(maker, root, "withdraw");
        await GlReject(checker, "/commands", GlInput(submitted, "approve", root), HttpStatusCode.PreconditionFailed, "STALE");
        var closed = await maker.Head(root);
        Assert.Equal("WITHDRAWN", closed.GetProperty("proposal").GetProperty("state").GetString());

        using var raceA = await Race(maker, secondMaker, closed);
        Assert.Equal(root, raceA.RootElement.GetProperty("rootId").GetGuid());
        var next = await maker.Head(root);
        Assert.NotEqual(firstProposal, next.GetProperty("proposal").GetProperty("id").GetGuid());
        var historical = await maker.Read($"/roots/{root:D}/history?proposalId={firstProposal:D}&revision=1");
        Assert.Equal(firstDigest, historical.GetProperty("version").GetProperty("digest").GetString());
        Assert.Equal(firstVersion, historical.GetProperty("version").GetProperty("id").GetGuid());
        Assert.Equal("HISTORY_ONLY", historical.GetProperty("kind").GetString());
        Assert.False(historical.GetProperty("isExecutionPermit").GetBoolean());

        var approved = await GlApproved(maker, checker, root);
        var datePreview = await GlPreview(checker, root, approved);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_fin_mfg_v1.ledgers SET business_date={day.AddDays(1)} WHERE (tenant_id,id)=({h.Tenant},{actual.Ledger})
            """);
        await GlReject(checker, "/commands", GlInput(approved, "publish", root,
            previewId: datePreview.GetProperty("previewId").GetGuid()), HttpStatusCode.PreconditionFailed, "STALE");
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_fin_mfg_v1.ledgers SET business_date={day} WHERE (tenant_id,id)=({h.Tenant},{actual.Ledger})
            """);
        var headPreview = await GlPreview(checker, root, approved);
        await GlCreate(maker, "SIM-DIFFERENT-ROOT", GlContent("同账套另一个草稿", day), false);
        await GlReject(checker, "/commands", GlInput(await checker.Head(root), "publish", root,
            previewId: headPreview.GetProperty("previewId").GetGuid()), HttpStatusCode.PreconditionFailed, "STALE");
        approved = await checker.Head(root);
        var oldPreview = await GlPreview(checker, root, approved);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_fin_gl_v1.books SET basis_key='SIM-PROFESSIONAL-COA-02'
            WHERE (tenant_id,ledger_id)=({h.Tenant},{actual.Ledger})
            """);
        await GlReject(checker, "/commands", GlInput(approved, "publish", root,
            previewId: oldPreview.GetProperty("previewId").GetGuid()), HttpStatusCode.PreconditionFailed, "STALE");
        await GlStep(maker, root, "withdraw");
        var second = await GlCreate(maker, "SIM-NOW", GlContent("并行账套版本", day), true);
        await GlRelease(maker, checker, second);
        var enabled = await checker.Head(second);
        var releaseId = enabled.GetProperty("timeline").GetProperty("releaseId").GetGuid();
        var approveId = enabled.GetProperty("decision").GetProperty("id").GetGuid();

        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_fin_gl_v1.books SET professional_available=false
            WHERE (tenant_id,ledger_id)=({h.Tenant},{actual.Ledger})
            """);
        await GlStep(maker, second, "suspend");
        var paused = await maker.Head(second);
        Assert.Equal("SUSPENDED", paused.GetProperty("controlState").GetString());
        Assert.Equal(releaseId, paused.GetProperty("timeline").GetProperty("releaseId").GetGuid());
        await GlReject(maker, "/commands", GlInput(paused, "restore", second, decisionId: approveId),
            HttpStatusCode.UnprocessableEntity, "RESTORE_DECISION_REQUIRED");
        await GlReject(checker, "/commands", GlInput(paused, "approve-restore", second),
            HttpStatusCode.ServiceUnavailable, "PROFESSIONAL_SERVICE_UNAVAILABLE");
        await GlReject(maker, "/commands", GlInput(await maker.Read("/roots"), "create", code: "SIM-ZERO",
            content: GlContent("零长度", day, day), activate: false), HttpStatusCode.UnprocessableEntity, "INVALID_EFFECTIVE_INTERVAL");
        await GlReject(maker, "/commands", GlInput(await maker.Read("/roots"), "create", code: "SIM-PAST",
            content: GlContent("过去日期", day.AddDays(-1), day), activate: false), HttpStatusCode.UnprocessableEntity, "INVALID_EFFECTIVE_INTERVAL");

        await DeviceEvidence("wp11-01-gl-revisions.json", new
        {
            oldContentDigestKept = true, afterSubmittedEdit = 409, oldDecision = 412, oneConcurrentSuccessor = true,
            staleProfessionalPreview = 412, staleBusinessDate = 412, staleScopeHead = 412,
            offlineEmergencyPause = 200, invalidRestoreDecision = 422,
            unavailableRestore = 503, invalidIntervals = new[] { "zero-length", "before-ledger-date" }
        });

        async Task<JsonDocument> Race(GlClient firstMaker, GlClient another, JsonElement start)
        {
            var outcomes = await Task.WhenAll(
                firstMaker.Send(HttpMethod.Post, "/commands", GlInput(start, "successor", root, GlContent("竞争后继甲", day), activate: true)),
                another.Send(HttpMethod.Post, "/commands", GlInput(start, "successor", root, GlContent("竞争后继乙", day), activate: true)));
            using var a = outcomes[0];
            using var b = outcomes[1];
            var winner = Assert.Single(outcomes, response => response.StatusCode == HttpStatusCode.OK);
            var loser = Assert.Single(outcomes, response => response.StatusCode == HttpStatusCode.PreconditionFailed);
            Assert.Equal("STALE", (await GlJson(loser, HttpStatusCode.PreconditionFailed)).GetProperty("message").GetString());
            return JsonDocument.Parse(await winner.Content.ReadAsByteArrayAsync());
        }
    }
}
