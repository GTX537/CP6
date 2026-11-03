using System.Net;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task App_business_keys_HTTP_preserves_100_Unicode_characters_and_rejects_101_without_writes()
    {
        await using var h = await Seed();
        var (owner, _, version) = await PrepareBinding(h);
        var biz = string.Concat(Enumerable.Repeat("😀", 100));
        var kind = new string('审', 100);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_approval_binding_v2.registrations
              (tenant_id,environment_id,biz_type,subject_kind,owner,revision,snapshot_schema,snapshot_fields,enabled,service_principal,audience,fixed_route,evidence_mode)
            SELECT tenant_id,environment_id,{biz},{kind},owner,{Guid.NewGuid()},snapshot_schema,snapshot_fields,enabled,service_principal,audience,fixed_route,evidence_mode
            FROM cp6_approval_binding_v2.registrations
            WHERE tenant_id={h.Tenant} AND environment_id={ScopeEnvironment} AND biz_type='TEST_DISPOSITION'
            """);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,
            new Dictionary<string,string> { ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString("D") });
        var cookies = await Login(host,h);
        var input = BindingInput(version) with { BizType = biz, SubjectKind = kind };
        using (var json = JsonDocument.Parse(BindingBytes(input)))
        using (var response = await host.Send(BoundRequest(cookies,"/api/oa/app/v2/bindings",json.RootElement)))
        {
            Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        }
        var original = await owner.OriginalAsync(input.CommandId);
        Assert.Equal(biz,original.Result.BizType); Assert.Equal(kind,original.Result.SubjectKind);
        Assert.Equal("Test Owner",original.Result.Owner);
        Assert.Equal(original.Result.BindingId,(await owner.SaveAsync(null,null,BindingBytes(input))).Result.BindingId);
        Assert.Equal(biz,Assert.Single((await owner.ListAsync()).Items).BizType);
        foreach (var bad in new[] { input with { CommandId=Guid.NewGuid(), BizType=biz+"x" }, input with { CommandId=Guid.NewGuid(), SubjectKind=kind+"x" } })
        {
            using var json = JsonDocument.Parse(BindingBytes(bad));
            using var response = await host.Send(BoundRequest(cookies,"/api/oa/app/v2/bindings",json.RootElement));
            Assert.Equal(HttpStatusCode.UnprocessableEntity,response.StatusCode);
        }
        Assert.Single((await owner.HistoryAsync(original.Result.BindingId)).Items);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_binding_v2.commands WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());
    }

    [Theory]
    [InlineData("a")]
    [InlineData("😀")]
    public void App_submission_wire_keeps_100_character_business_keys_and_rejects_101(string rune)
    {
        var key = string.Concat(Enumerable.Repeat(rune,100));
        var bytes = Encoding.UTF8.GetBytes("{}"); var digest = ApprovalCanonicalV2.Digest(bytes);
        var command = new ApprovalSubmitCommandV2("oa.app/2.0",new("test",Guid.NewGuid()),Guid.NewGuid(),Guid.NewGuid(),
            new("Test Owner",key,key,"subject","1",digest),new(new("Test Owner","policy","1"),"1",digest),null,null,
            Convert.ToBase64String(new byte[8]),Guid.NewGuid(),new("Test Owner","authorization","1"),[],
            new("test/1",Convert.ToBase64String(bytes),digest,[]),null,"",Guid.NewGuid(),DateTimeOffset.FromUnixTimeMilliseconds(1000));
        var parsed = ApprovalSubmissionWireV2.Parse(ApprovalCanonicalV2.Bytes(command));
        Assert.Equal(key,parsed.Value.Subject.BizType); Assert.Equal(key,parsed.Value.Subject.SubjectKind);
        foreach (var subject in new[] { command.Subject with { BizType=key+"x" }, command.Subject with { SubjectKind=key+"x" } })
            Assert.Equal("SUBMISSION_SHAPE_INVALID",Assert.Throws<ScopeAuthorizationExceptionV1>(()=>
                ApprovalSubmissionWireV2.Parse(ApprovalCanonicalV2.Bytes(command with { Subject=subject }))).Message);
    }
}
