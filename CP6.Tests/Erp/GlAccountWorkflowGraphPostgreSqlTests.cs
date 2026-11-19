using System.Net;
using System.Text.Json;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<JsonElement> GlApproved(GlClient maker, GlClient checker, Guid root)
    {
        await GlStep(maker, root, "submit");
        await GlStep(checker, root, "approve");
        return await checker.Head(root);
    }

    private static async Task<JsonElement> GlRelease(GlClient maker, GlClient checker, Guid root)
    {
        var head = await GlApproved(maker, checker, root);
        var preview = await GlPreview(checker, root, head);
        return await checker.Write("/commands", GlInput(head, "publish", root, previewId: preview.GetProperty("previewId").GetGuid()));
    }

    private static async Task GlReject(GlClient client, string route, object input, HttpStatusCode status, string message)
        => Assert.Equal(message, (await client.Write(route, input, status)).GetProperty("message").GetString());

    private static object GlPreviewInput(JsonElement head, bool? activate = null)
    {
        var version = head.GetProperty("version").GetProperty("content");
        return new
        {
            expectedScopeRevision = head.GetProperty("scopeRevision").GetInt64(),
            expectedHeads = head.GetProperty("heads"),
            activate = activate ?? head.GetProperty("proposal").GetProperty("activate").GetBoolean(),
            replaceFrom = version.GetProperty("from"),
            replaceToExclusive = version.GetProperty("toExclusive")
        };
    }

    private static string GlMessage(JsonElement result, string property = "reason") => result.GetProperty(property).GetString()!;

    [CurrentPermissionPostgresFact]
    public async Task Gl_owner_scans_child_and_role_intervals_and_rejects_invalid_new_uses()
    {
        await using var h = await Seed();
        var actual = await SeedGlBoard(h, "graph");
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, GlHostSettings());
        var maker = new GlClient(host, await Login(host, h));
        var checker = new GlClient(host, await Login(host, "gl-checker-graph", "Price1!Password", h.Tenant.ToString("N")));
        await maker.AuthenticateBook();
        await checker.AuthenticateBook();

        var day = GlBusinessDay;
        var group = await GlCreate(maker, "graph-group", GlContent("模拟父组", day, postingClass: "GROUP"), true);
        await GlRelease(maker, checker, group);
        var group2 = await GlCreate(maker, "graph-child-group",
            GlContent("模拟子组", day, parentRootId: group, postingClass: "GROUP"), true);
        await GlRelease(maker, checker, group2);
        var parents = await maker.Read($"/parent-options?date={day:yyyy-MM-dd}");
        Assert.Contains(parents.EnumerateArray(), row => row.GetProperty("id").GetGuid() == group);

        var child = await GlCreate(maker, "graph-expense", GlContent("模拟期间科目", day.AddDays(7), day.AddDays(20),
            parentRootId: group2, roleKey: "EXPENSE"), true);
        await GlRelease(maker, checker, child);
        var same = GlInput(await maker.Read("/roots"), "create", code: "GRAPH-EXPENSE",
            content: GlContent("冲突名称", day), activate: false);
        await GlReject(maker, "/commands", same, HttpStatusCode.Conflict, "CODE_CONFLICT");

        var rolled = GlInput(await maker.Head(group), "successor", group,
            content: GlContent("自指草稿", day, parentRootId: group, postingClass: "GROUP"), activate: false);
        await GlReject(maker, "/commands", rolled, HttpStatusCode.UnprocessableEntity, "PARENT_CYCLE");
        await GlStep(maker, group, "successor", GlContent("全区间环候选", day, parentRootId: group2, postingClass: "GROUP"), false);
        var circular = await GlApproved(maker, checker, group);
        await GlReject(checker, $"/roots/{group:D}/publish-preview", GlPreviewInput(circular),
            HttpStatusCode.UnprocessableEntity, "PARENT_CYCLE");
        await GlStep(maker, group, "withdraw");
        await GlStep(maker, group2, "successor", GlContent("中途改成记账类", day.AddDays(12), day.AddDays(25)), false);
        var flip = await GlApproved(maker, checker, group2);
        await GlReject(checker, $"/roots/{group2:D}/publish-preview", GlPreviewInput(flip),
            HttpStatusCode.UnprocessableEntity, "CHILD_INTERVAL_CONFLICT");

        var date = day.AddDays(19);
        var overlap = await GlCreate(maker, "graph-overlap", GlContent("重叠角色候选", date, day.AddDays(25), roleKey: "EXPENSE"), true);
        var overlapHead = await GlApproved(maker, checker, overlap);
        var blocked = await GlPreview(checker, overlap, overlapHead);
        Assert.False(blocked.GetProperty("canApply").GetBoolean());
        Assert.Contains("ROLE_INTERVAL_CONFLICT", blocked.GetProperty("issues").EnumerateArray().Select(value => value.GetString()));
        Assert.Single(blocked.GetProperty("segments").EnumerateArray());
        await GlReject(checker, "/commands", GlInput(overlapHead, "publish", overlap,
            previewId: blocked.GetProperty("previewId").GetGuid()), HttpStatusCode.UnprocessableEntity, "ROLE_INTERVAL_CONFLICT");
        await GlStep(maker, child, "suspend");
        var stillBlocked = await GlPreview(checker, overlap, await checker.Head(overlap));
        Assert.False(stillBlocked.GetProperty("canApply").GetBoolean());
        var adjacent = await GlCreate(maker, "graph-adjacent", GlContent("相邻角色", day.AddDays(20), roleKey: "EXPENSE"), true);
        await GlRelease(maker, checker, adjacent);

        Assert.Equal("NOT_CONFIGURED", GlMessage(await maker.Read("/roles/EXPENSE"), "status"));
        Assert.Equal(child, (await maker.Read($"/roles/EXPENSE?date={date:yyyy-MM-dd}")).GetProperty("rootId").GetGuid());
        Assert.Equal(adjacent, (await maker.Read($"/roles/EXPENSE?date={day.AddDays(20):yyyy-MM-dd}")).GetProperty("rootId").GetGuid());

        var groupScreen = await maker.Read($"/roots/{group:D}/eligibility?purpose=GL_MANUAL&currency=CNY");
        Assert.Equal("GROUP_NOT_POSTABLE", GlMessage(groupScreen));
        var absent = await maker.Read($"/roots/{adjacent:D}/eligibility?purpose=GL_MANUAL&currency=CNY");
        Assert.Equal("NO_EFFECTIVE_VERSION", GlMessage(absent));
        var definition = await GlCreate(maker, "graph-control", GlContent("模拟应收控制科目", day,
            subLedgerType: "AR", requirePartner: true, roleKey: "AR_CONTROL"), true);
        await GlRelease(maker, checker, definition);
        Assert.Equal("CONTROL_ACCOUNT_MANUAL_DENIED", GlMessage(await maker.Read($"/roots/{definition:D}/eligibility?purpose=GL_MANUAL&currency=CNY")));
        Assert.Equal("SUBLEDGER_AUTHORITY_UNPROVEN", GlMessage(await maker.Read($"/roots/{definition:D}/eligibility?purpose=GL_AUTO&currency=CNY")));
        var partner = await GlCreate(maker, "graph-partner", GlContent("模拟往来科目", day, requirePartner: true), true);
        await GlRelease(maker, checker, partner);
        var partnerCheck = await maker.Read($"/roots/{partner:D}/eligibility?purpose=GL_MANUAL&currency=CNY&partnerId=simulated-partner");
        Assert.Equal("PARTNER_AUTHORITY_UNPROVEN", GlMessage(partnerCheck));
        Assert.False(partnerCheck.GetProperty("isExecutionPermit").GetBoolean());
        var single = await GlCreate(maker, "graph-currency", GlContent("模拟外币专户", day, currencyMode: "SINGLE", currencies: ["USD"]), true);
        await GlRelease(maker, checker, single);
        Assert.Equal("CURRENCY_MISMATCH", GlMessage(await maker.Read($"/roots/{single:D}/eligibility?purpose=GL_MANUAL&currency=CNY")));
        Assert.Equal("OBSERVED_AS_OF", GlMessage(await maker.Read($"/roots/{single:D}/eligibility?purpose=GL_MANUAL&currency=USD"), "status"));
        Assert.Equal("CURRENCY_MISMATCH", GlMessage(await maker.Read($"/roots/{adjacent:D}/eligibility?purpose=GL_MANUAL&currency=JPY&date={day.AddDays(20):yyyy-MM-dd}")));

        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_fin_mfg_v1.ledgers SET business_date={day.AddDays(13)}
            WHERE (tenant_id,id)=({h.Tenant},{actual.Ledger})
            """);
        await GlStep(maker, child, "retire");
        Assert.Equal("NOT_CONFIGURED", GlMessage(await maker.Read("/roles/EXPENSE"), "status"));
        Assert.Equal(child, (await maker.Read($"/roles/EXPENSE?date={day.AddDays(12):yyyy-MM-dd}")).GetProperty("rootId").GetGuid());
        Assert.Equal("RETIRED", GlMessage(await maker.Read($"/roots/{child:D}/eligibility?purpose=GL_MANUAL&currency=CNY")));

        // 模拟已污染的源投影。用相邻版本的精确版本引用构造故障样本，验证查角色时绝不取首行。
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_gl_v1.segments(tenant_id,ledger_id,root_id,revision,valid_from,to_exclusive,version_id,release_id)
            SELECT s.tenant_id,s.ledger_id,s.root_id,s.revision,{day.AddDays(7)},{day.AddDays(13)},s.version_id,s.release_id
            FROM cp6_fin_gl_v1.segments s
            WHERE (s.tenant_id,s.ledger_id,s.root_id,s.valid_from)=({h.Tenant},{actual.Ledger},{adjacent},{day.AddDays(20)})
            """);
        var unproven = await maker.Read($"/roles/EXPENSE?date={day.AddDays(12):yyyy-MM-dd}");
        Assert.Equal("UNPROVEN", GlMessage(unproven, "status"));
        Assert.Equal(JsonValueKind.Null, unproven.GetProperty("rootId").ValueKind);

        await DeviceEvidence("wp11-01-gl-intervals.json", new
        {
            parentDateTransition = 422, cycle = 422, duplicateCode = 409, overlapBeforeAndWhilePaused = 422,
            adjacentIntervals = true, zeroRole = "NOT_CONFIGURED", corruptedRole = "UNPROVEN",
            group = GlMessage(groupScreen), gap = GlMessage(absent), partner = GlMessage(partnerCheck),
            retiredRoleCutoff = true
        });
    }
}
