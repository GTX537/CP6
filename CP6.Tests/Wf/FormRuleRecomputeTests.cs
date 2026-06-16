using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

/// <summary>
/// OA 章06 §6 表单规则后端复算（B-2，OA2-D2）。铁律：前端体验、后端为准——提交时按 rules
/// 重算 compute（落库以后端为准）、按生效 required 复核、隐藏字段免校验。与前端 ruleEngine 同语义。
/// </summary>
public class FormRuleRecomputeTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    private static FormService Svc(CP6Context db) => new(db);

    // 请假单：days = dateDiff(end,start)+1（compute）；type=='other' 时 reason 变必填（require）
    private const string LeaveSchema = """
    {
      "fields": [
        {"name":"type","label":"类型","type":"select","required":true},
        {"name":"start","label":"开始","type":"date","required":true},
        {"name":"end","label":"结束","type":"date","required":true},
        {"name":"days","label":"天数","type":"number"},
        {"name":"reason","label":"事由","type":"input"}
      ],
      "rules": [
        {"when":"", "then":[{"action":"compute","target":"days","expr":"dateDiff(end, start) + 1"}]},
        {"when":"type == 'other'", "then":[{"action":"require","target":"reason"}]}
      ]
    }
    """;

    [Fact]
    public void Recompute_ComputesDays_ServerAuthoritative()
    {
        using var db = NewDb();
        // 前端传了个错的 days=999，后端应按 compute 覆盖为 dateDiff+1 = 6
        var (json, errors) = Svc(db).RecomputeAndValidate(LeaveSchema,
            """{"type":"annual","start":"2026-06-10","end":"2026-06-15","days":999}""");

        Assert.Empty(errors);
        Assert.Contains("\"days\":6", json);
        Assert.DoesNotContain("999", json);
    }

    [Fact]
    public void Recompute_ConditionalRequire_BlocksWhenMissing()
    {
        using var db = NewDb();
        var (_, errors) = Svc(db).RecomputeAndValidate(LeaveSchema,
            """{"type":"other","start":"2026-06-10","end":"2026-06-11"}""");   // type=other 但缺 reason

        Assert.Contains(errors, e => e.Contains("事由") && e.Contains("必填"));
    }

    [Fact]
    public void Recompute_ConditionalRequire_PassesWhenRuleNotTriggered()
    {
        using var db = NewDb();
        var (_, errors) = Svc(db).RecomputeAndValidate(LeaveSchema,
            """{"type":"annual","start":"2026-06-10","end":"2026-06-11"}""");   // 非 other → reason 不必填

        Assert.Empty(errors);
    }

    [Fact]
    public void Recompute_HiddenField_SkipsValidation()
    {
        using var db = NewDb();
        const string schema = """
        {
          "fields": [
            {"name":"needInvoice","label":"需发票","type":"checkbox"},
            {"name":"invoiceTitle","label":"发票抬头","type":"input","required":true}
          ],
          "rules": [
            {"when":"needInvoice == false", "then":[{"action":"hide","target":"invoiceTitle"}]}
          ]
        }
        """;
        // needInvoice=false → invoiceTitle 被隐藏 → 即便 required 也免校验
        var (_, errors) = Svc(db).RecomputeAndValidate(schema, """{"needInvoice":false}""");
        Assert.Empty(errors);

        // needInvoice=true → invoiceTitle 仍必填 → 缺则报错
        var (_, errors2) = Svc(db).RecomputeAndValidate(schema, """{"needInvoice":true}""");
        Assert.Contains(errors2, e => e.Contains("发票抬头") && e.Contains("必填"));
    }

    [Fact]
    public async Task SubmitData_StoresRecomputedValue()
    {
        using var db = NewDb();
        var svc = Svc(db);
        await svc.SaveDefAsync("leave", "请假单", LeaveSchema);

        await svc.SubmitDataAsync("leave", null,
            """{"type":"annual","start":"2026-06-10","end":"2026-06-15","days":1}""");

        var data = await db.Wf_FormDatas.SingleAsync();
        Assert.Contains("\"days\":6", data.DataJson);   // 落库为后端复算值，非前端传入的 1
    }

    [Fact]
    public void Recompute_NoRules_BehavesLikePlainValidate()
    {
        using var db = NewDb();
        const string schema = """{"fields":[{"name":"reason","label":"事由","type":"input","required":true}]}""";
        var (json, errors) = Svc(db).RecomputeAndValidate(schema, """{"reason":"年假"}""");
        Assert.Empty(errors);
        Assert.Contains("年假", json);

        var (_, errMissing) = Svc(db).RecomputeAndValidate(schema, "{}");
        Assert.Contains(errMissing, e => e.Contains("必填"));
    }
}
