using System.Net;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Gl_owner_published_parent_chain_accepts_32_and_rejects_33_and_closed_inputs()
    {
        await using var h = await Seed();
        await SeedGlBoard(h, "depth");
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, GlHostSettings());
        var maker = new GlClient(host, await Login(host, h));
        var checker = new GlClient(host, await Login(host, "gl-checker-depth", "Price1!Password", h.Tenant.ToString("N")));
        await maker.AuthenticateBook();
        await checker.AuthenticateBook();
        var day = GlBusinessDay;

        var root = await GlCreate(maker, "Depth-01", GlContent("第 1 层模拟分组科目", day, postingClass: "GROUP"), true);
        await GlRelease(maker, checker, root);
        for (var level = 2; level <= 32; level++)
        {
            root = await GlCreate(maker, $"DEPTH-{level:D2}", GlContent($"第 {level} 层模拟分组科目", day,
                parentRootId: root, postingClass: "GROUP"), true);
            await GlRelease(maker, checker, root);
        }
        Assert.Equal(32, (await maker.Head(root)).GetProperty("currentEffective").GetProperty("level").GetInt32());
        var deepest = await GlCreate(maker, "DEPTH-33", GlContent("第 33 层模拟科目", day, parentRootId: root), true);
        var approved = await GlApproved(maker, checker, deepest);
        await GlReject(checker, $"/roots/{deepest:D}/publish-preview", GlPreviewInput(approved),
            HttpStatusCode.UnprocessableEntity, "DEPENDENCY_LIMIT");
        Assert.Equal("SUSPENDED", (await checker.Head(deepest)).GetProperty("controlState").GetString());

        var revision = await GlScopeRevision(maker);
        using var banned = await maker.Send(HttpMethod.Post, "/commands", new
        {
            operationRef = Guid.NewGuid(), kind = "create", expectedScopeRevision = revision,
            code = "NONE", content = GlContent("闭集输入示例", day), activate = false, reason = "模拟封闭契约",
            tenantId = Guid.NewGuid(), creator = h.User, state = "APPROVED"
        });
        Assert.Equal(HttpStatusCode.BadRequest, banned.StatusCode);
        await GlReject(maker, "/commands", GlInput(await maker.Read("/roots"), "create", code: "坏-GL",
            content: GlContent("非 ASCII 编码", day), activate: false), HttpStatusCode.UnprocessableEntity, "ACCOUNT_CODE_INVALID");
        Assert.Equal(revision, await GlScopeRevision(maker));

        await DeviceEvidence("wp11-01-gl-boundaries.json", new { deepestPublished = 32, overLimit = 422, closedDto = 400 });
    }
}
