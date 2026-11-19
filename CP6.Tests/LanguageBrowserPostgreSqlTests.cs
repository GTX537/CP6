using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task Language_browser_exact_publication_recovery_and_switch_use_real_main()
    {
        await using var admin = await TenantManagementSeed();
        await using var other = await Seed();
        await LanguageActions(admin);
        await LanguageActions(other);
        var suffix = Guid.NewGuid().ToString("N");
        var key = "plm.browser.language-" + suffix;
        admin.Db.Sys_Langs.Add(new Sys_Lang
        {
            LangKey = key, Status = "reviewed", ZhCN = "公共引用 {ref}", ZhTW = "公共引用 {ref}",
            En = "Public reference {ref}", Ja = "公開参照 {ref}", Ko = "공개 참조 {ref}"
        });
        await admin.Db.SaveChangesAsync();
        var first = "第一组织 {ref}";
        var second = "第二组织 {ref}";
        await admin.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_lang_v1.tenant_entries("TenantId","LangKey","Status","ZhCN")
            VALUES ({admin.Tenant},{key},'reviewed',{first}),({other.Tenant},{key},'reviewed',{second})
            """);
        var root = Path.Combine(Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!, "language-browser-files-" + suffix);
        using var evidence = await BrowserScenario(admin, other.User, "language-browser.mjs", "language-browser",
            new() { ["Storage__LocalRoot"] = root }, new()
            {
                ["CP6_LANG_SECOND_TENANT"] = other.Tenant.ToString("N"),
                ["CP6_LANG_TEST_KEY"] = key
            });
        var version = evidence.RootElement.GetProperty("firstVersion").GetString();
        var current = await admin.Db.Database.SqlQuery<string>($"""
            SELECT version AS "Value" FROM cp6_lang_v1.pointer WHERE singleton
            """).SingleAsync();
        Assert.Equal(version, current);
    }
}
