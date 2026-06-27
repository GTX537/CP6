using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class CatalogServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static IFavoriteService Fav(CP6Context db) => new FavoriteService(db);
    private static ICatalogService Cat(CP6Context db) => new CatalogService(db, Fav(db));

    private static async Task SeedFormsAsync(CP6Context db)
    {
        db.Wf_FormDefs.AddRange(
            new Wf_FormDef { Id = Guid.NewGuid(), FormKey = "leave", FormName = "请假单", Category = "人事", SubCategory = "假勤", Enable = true },
            new Wf_FormDef { Id = Guid.NewGuid(), FormKey = "expense", FormName = "报销单", Category = "财务", SubCategory = "费用", Enable = true },
            new Wf_FormDef { Id = Guid.NewGuid(), FormKey = "off", FormName = "停用单", Category = "人事", SubCategory = "假勤", Enable = false }); // 停用不入库
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Catalog_GroupsByCategory_FlagsFavorite()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        await SeedFormsAsync(db);
        await Fav(db).AddAsync(me, "leave");

        var tree = await Cat(db).CatalogAsync(me);
        Assert.Equal(2, tree.Count);                                   // 人事 / 财务
        var hr = tree.Single(n => n.Category == "人事");
        var card = hr.Subs.Single().Forms.Single(f => f.FormKey == "leave");
        Assert.True(card.Favorite);
        Assert.DoesNotContain(tree.SelectMany(n => n.Subs).SelectMany(s => s.Forms), f => f.FormKey == "off"); // 停用排除
    }

    [Fact]
    public async Task Favorite_AddIdempotent_AndRemove()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        await SeedFormsAsync(db);
        await Fav(db).AddAsync(me, "leave");
        await Fav(db).AddAsync(me, "leave");                           // 幂等：不重复
        Assert.Single(await Fav(db).ListAsync(me));
        await Fav(db).RemoveAsync(me, "leave");
        Assert.Empty(await Fav(db).ListAsync(me));
    }
}
