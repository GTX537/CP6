using System.Net;
using System.Text.Json;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Dictionary_owner_versions_original_and_referenced_history_are_stable()
    {
        await using var h = await DictionarySeed();
        var code = DictionaryCode(h, "A");
        var duplicate = DictionaryCode(h, "B");
        var typeId = await h.Db.Sys_DictTypes.Where(row => row.TypeCode == code).Select(row => row.Id).SingleAsync();
        var itemId = await h.Db.Sys_DictDatas.Where(row => row.TypeCode == code && row.Value == "A").Select(row => row.Id).SingleAsync();
        var context = await PreparePersonal(h, OptionFormSchema(typeId));
        var personal = await context.Item1.ContextAsync("personal");

        var connection = h.Db.Database.GetConnectionString()!;
        await using var api = await AuthenticationApiProcess.Start(connection);
        await using var replica = await AuthenticationApiProcess.Start(connection);
        var a = await Login(api, h);
        var b = await Login(replica, h);
        const string route = DictionaryPath + "/owner-v1";
        async Task<JsonElement> Read(AuthenticationApiProcess host, Dictionary<string, string> identity, string target)
        {
            using var reply = await host.Send(OperationsRequest(identity, HttpMethod.Get, route + target));
            return await LanguageJson(reply, HttpStatusCode.OK);
        }

        var start = await Read(api, a, "/options/" + code + "?historicalValue=OFF");
        var version = start.GetProperty("dictionaryVersion").GetString();
        var item = Assert.Single(start.GetProperty("items").EnumerateArray());
        Assert.Equal("A", item.GetProperty("value").GetString());
        Assert.False(start.GetProperty("historical").GetProperty("selectable").GetBoolean());
        var old = await Read(api, a, "/options/" + code + "?historicalValue=unknown");
        Assert.Equal(JsonValueKind.Null, old.GetProperty("historical").ValueKind);

        object Edit(Guid operation, string label, string? expected, bool enable, string value = "A") => new
        {
            operationRef = operation, kind = "update-data", typeCode = code, expectedVersion = expected,
            id = itemId, value, label, enable, orderNo = 1, reason = "演练字典修订"
        };
        Task<HttpResponseMessage> Apply(AuthenticationApiProcess host, Dictionary<string, string> actor, object body) =>
            host.Send(OperationsRequest(actor, HttpMethod.Post, route + "/commands", body));

        var left = Guid.NewGuid();
        var right = Guid.NewGuid();
        var inputA = Edit(left, "保留原编码", version, true);
        var inputB = Edit(right, "更新标签", version, true);
        var replies = await Task.WhenAll(Apply(api, a, inputA), Apply(replica, b, inputB));
        using var first = replies[0];
        using var second = replies[1];
        Assert.Single(replies, reply => reply.StatusCode == HttpStatusCode.OK);
        Assert.Single(replies, reply => reply.StatusCode == HttpStatusCode.PreconditionFailed);
        var operation = first.StatusCode == HttpStatusCode.OK ? left : right;
        var label = first.StatusCode == HttpStatusCode.OK ? "保留原编码" : "更新标签";
        var input = first.StatusCode == HttpStatusCode.OK ? inputA : inputB;
        var after = await Read(replica, b, "/options/" + code);
        var next = after.GetProperty("dictionaryVersion").GetString();
        Assert.NotEqual(version, next);
        Assert.Equal("A", after.GetProperty("items")[0].GetProperty("value").GetString());
        Assert.Equal(label, after.GetProperty("items")[0].GetProperty("label").GetString());
        var original = await Read(replica, b, "/operations/" + operation);
        using (var replay = await Apply(api, a, input))
            Assert.Equal(original.GetRawText(), (await LanguageJson(replay, HttpStatusCode.OK)).GetRawText());
        using (var changeKey = await Apply(api, a, Edit(Guid.NewGuid(), "错误替换编码", next, true, "changed")))
            Assert.Equal(HttpStatusCode.Conflict, changeKey.StatusCode);
        using (var addExisting = await Apply(api, a, new
        {
            operationRef = Guid.NewGuid(), kind = "create-data", typeCode = code, expectedVersion = next,
            value = "A", label = "重复编码", enable = true, orderNo = 1, reason = "验收重复"
        }))
            Assert.Equal(HttpStatusCode.Conflict, addExisting.StatusCode);
        using (var rename = await Apply(api, a, new
        {
            operationRef = Guid.NewGuid(), kind = "update-type", typeCode = duplicate, expectedVersion = next,
            id = typeId, label = "尝试改名", enable = true, orderNo = 1, reason = "验收稳定类型编码"
        }))
            Assert.Equal(HttpStatusCode.Conflict, rename.StatusCode);

        using (var blocked = await Apply(api, a, new
        {
            operationRef = Guid.NewGuid(), kind = "delete-data", typeCode = code,
            expectedVersion = next, id = itemId, reason = "验收引用保护"
        }))
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        using (var blocked = await api.Send(OperationsRequest(a, HttpMethod.Delete, DictionaryPath + "/types", new[] { typeId })))
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        using (var disabled = await Apply(api, a, Edit(Guid.NewGuid(), label, next, false)))
            await LanguageJson(disabled, HttpStatusCode.OK);
        var retired = await Read(replica, b, "/options/" + code + "?historicalValue=A");
        Assert.Empty(retired.GetProperty("items").EnumerateArray());
        Assert.Equal(label, retired.GetProperty("historical").GetProperty("label").GetString());
        Assert.False(retired.GetProperty("historical").GetProperty("selectable").GetBoolean());
        var error = await Assert.ThrowsAsync<CP6.Core.Services.Sys.ScopeAuthorizationExceptionV1>(() =>
            context.Item1.SubmitDirectAsync("personal", PersonalCommand(PersonalDirect(personal, "{\"choice\":\"A\"}"), "dictionary-stopped")));
        Assert.Equal("FORM_OPTION_VALUE_UNAVAILABLE", error.Message);

        await DeviceEvidence("wp02-17-dictionary-versions.json", new
        {
            race = replies.Select(reply => (int)reply.StatusCode), original = operation,
            before = version, after = next, stableValue = "A", historicalLabel = label,
            protectedTypeAndValue = true, stoppedInOwnerCode = error.Message, unknownFirstDefault = false
        });
    }
}
