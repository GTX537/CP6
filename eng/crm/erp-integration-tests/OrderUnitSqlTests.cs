using CP6.Core.Services.ErpIntegration;
using Microsoft.EntityFrameworkCore;

namespace CP6.ErpIntegration.SqlTests;

[Collection(SqlDatabaseCollection.Name)]
public sealed class OrderUnitSqlTests(SqlDatabaseFixture database)
{
    [Theory]
    [InlineData("kg", "g", "g", false)]
    [InlineData("PCS", "PCS", "BOX", false)]
    [InlineData("PCS", null, "PCS", false)]
    [InlineData("PCS", "PCS", null, false)]
    [InlineData("PCS", "PCS", " ", false)]
    [InlineData("kg", "KG", "KG", false)]
    [InlineData("kg", "kg", "kg", true)]
    [InlineData("PCS", "PCS", "PCS", true)]
    public async Task Accepted_quantity_and_price_require_identical_explicit_units(
        string quotationUnit, string? quantityUnit, string? priceUnit, bool valid)
    {
        var s = new ErpScenario(database);
        await s.InitializeAsync();
        var request = await s.ReadyOrderAsync(unit: quotationUnit);
        int sequenceBefore;
        await using (var db = s.Db())
        {
            var quote = await db.Quotations.Include(x => x.Details).SingleAsync();
            Assert.Equal(quotationUnit, Assert.Single(quote.Details).Unit);
            Assert.True(ErpQuotationAcceptance.IsCurrent(quote, s.Clock.GetUtcNow()));
            var product = await db.ProductMasters.SingleAsync();
            product.QtyUnit = quantityUnit;
            product.UnitPriceUnit = priceUnit;
            await db.SaveChangesAsync();
            sequenceBefore = await db.DocSequences.Where(x => x.FuncCode == "ORD").Select(x => x.LastSeq).SingleOrDefaultAsync();
        }

        var envelope = s.Envelope(request);
        await s.ConsumeAsync(envelope);
        if (valid)
        {
            var journal = await s.JournalAsync(request.OpportunityId);
            Assert.True(journal.Terminal && journal.Succeeded, journal.ResultDataJson);
            await using var db = s.Db();
            var line = await db.OrderDetails.SingleAsync();
            Assert.Equal(quotationUnit, line.QtyUnit);
            Assert.Equal(quotationUnit, line.UnitPriceUnit);
            Assert.Equal(2m, line.Quantity);
            Assert.Equal(150m, line.IndividualUnitPrice);
            Assert.Equal(300m, line.Amount);
            var result = Assert.Single(await s.ResultsAsync(ErpEventContracts.OrderCreated));
            Assert.Equal(300m, result.GetProperty("data").GetProperty("bookedAmount").GetDecimal());
            Assert.Equal(sequenceBefore + 1, await db.DocSequences.Where(x => x.FuncCode == "ORD").Select(x => x.LastSeq).SingleAsync());
        }
        else
        {
            await s.AssertTerminalAsync(request.OpportunityId, "C03_QUOTATION_UNIT_MISMATCH", true);
            var original = await s.JournalAsync(request.OpportunityId);
            await s.ConsumeAsync(envelope);
            await s.ConsumeAsync(s.Envelope(request));
            var repeated = await s.JournalAsync(request.OpportunityId);
            Assert.Equal(original.ResultDataJson, repeated.ResultDataJson);
            Assert.Equal(original.RowVersion, repeated.RowVersion);
            await s.AssertNoOrderAsync();
            await using var db = s.Db();
            Assert.Equal(sequenceBefore, await db.DocSequences.Where(x => x.FuncCode == "ORD").Select(x => x.LastSeq).SingleOrDefaultAsync());
        }
    }
}
