using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using CP6.Tests.Infra;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Fin;

/// <summary>
/// Executable proof that BudgetVersion is the aggregate concurrency boundary:
/// two writers changing different BudgetLine buckets still serialize on the
/// same native SQL Server rowversion token.
/// </summary>
public sealed class BudgetLineVersionConcurrencySqlServerTests : IDisposable
{
    private readonly string? _connectionString;

    public BudgetLineVersionConcurrencySqlServerTests()
    {
        var configured = Environment.GetEnvironmentVariable(SqlServerFactAttribute.EnvVar);
        if (string.IsNullOrWhiteSpace(configured)) return;

        _connectionString = new SqlConnectionStringBuilder(configured)
        {
            InitialCatalog = $"CP6Test_FinBudget_{Guid.NewGuid():N}",
        }.ConnectionString;

        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    [SqlServerFact]
    public async Task Stale_version_token_rejects_second_writer_on_a_different_line()
    {
        var (versionId, firstAccountId, secondAccountId) = await SeedAsync();

        await using var firstWriter = NewContext();
        await using var secondWriter = NewContext();
        var firstToken = (await firstWriter.BudgetVersions.AsNoTracking()
            .SingleAsync(v => v.Id == versionId)).RowVersion!;
        var staleSecondToken = (await secondWriter.BudgetVersions.AsNoTracking()
            .SingleAsync(v => v.Id == versionId)).RowVersion!;
        Assert.Equal(firstToken, staleSecondToken);

        var firstResult = await new BudgetLineService(firstWriter).UpsertLineAsync(new BudgetLineDto
        {
            VersionId = versionId,
            AccountId = firstAccountId,
            AnnualAmount = 1200m,
            SpreadMode = "even",
            VersionRowVersion = firstToken,
        });
        Assert.True(firstResult.Ok);

        var staleResult = await new BudgetLineService(secondWriter).UpsertLineAsync(new BudgetLineDto
        {
            VersionId = versionId,
            AccountId = secondAccountId,
            AnnualAmount = 600m,
            SpreadMode = "even",
            VersionRowVersion = staleSecondToken,
        });
        Assert.False(staleResult.Ok);
        Assert.Equal("E-A5-CONCURRENCY-001", staleResult.Code);

        await using (var assertion = NewContext())
        {
            var lines = await assertion.BudgetLines.AsNoTracking()
                .Where(l => l.VersionId == versionId)
                .ToListAsync();
            Assert.Single(lines);
            Assert.Equal(firstAccountId, lines[0].AccountId);
        }

        await using var retryWriter = NewContext();
        var freshToken = (await retryWriter.BudgetVersions.AsNoTracking()
            .SingleAsync(v => v.Id == versionId)).RowVersion!;
        Assert.NotEqual(staleSecondToken, freshToken);
        var retryResult = await new BudgetLineService(retryWriter).UpsertLineAsync(new BudgetLineDto
        {
            VersionId = versionId,
            AccountId = secondAccountId,
            AnnualAmount = 600m,
            SpreadMode = "even",
            VersionRowVersion = freshToken,
        });
        Assert.True(retryResult.Ok);

        await using var deleteSnapshot = NewContext();
        var deleteVersionToken = (await deleteSnapshot.BudgetVersions.AsNoTracking()
            .SingleAsync(v => v.Id == versionId)).RowVersion!;
        var firstLine = await deleteSnapshot.BudgetLines.AsNoTracking()
            .SingleAsync(l => l.VersionId == versionId && l.AccountId == firstAccountId);

        await using var competingWriter = NewContext();
        var competingVersionToken = (await competingWriter.BudgetVersions.AsNoTracking()
            .SingleAsync(v => v.Id == versionId)).RowVersion!;
        var secondLine = await competingWriter.BudgetLines.AsNoTracking()
            .SingleAsync(l => l.VersionId == versionId && l.AccountId == secondAccountId);
        var competingResult = await new BudgetLineService(competingWriter).UpsertLineAsync(new BudgetLineDto
        {
            VersionId = versionId,
            AccountId = secondAccountId,
            AnnualAmount = 700m,
            SpreadMode = "even",
            RowVersion = secondLine.RowVersion,
            VersionRowVersion = competingVersionToken,
        });
        Assert.True(competingResult.Ok);

        await using var staleDeleteWriter = NewContext();
        var staleDelete = await new BudgetLineService(staleDeleteWriter).DeleteLineAsync(
            firstLine.Id,
            firstLine.RowVersion,
            deleteVersionToken);
        Assert.False(staleDelete.Ok);
        Assert.Equal("E-A5-CONCURRENCY-001", staleDelete.Code);

        await using var deleteAssertion = NewContext();
        Assert.True(await deleteAssertion.BudgetLines.AsNoTracking()
            .AnyAsync(l => l.Id == firstLine.Id));
    }

    private async Task<(Guid VersionId, Guid FirstAccountId, Guid SecondAccountId)> SeedAsync()
    {
        await using var context = NewContext();
        var budgetId = Guid.NewGuid();
        var budget = new Budget
        {
            Id = budgetId,
            No = "BUD-2027-90001",
            Name = "Concurrency proof",
            FiscalYear = 2027,
            IsActive = true,
        };
        var version = new BudgetVersion
        {
            Id = Guid.NewGuid(),
            BudgetId = budgetId,
            VersionNo = 1,
            Name = "Draft",
            Status = BudgetVersionStatus.Draft,
        };
        var firstAccount = NewExpenseAccount("660201");
        var secondAccount = NewExpenseAccount("660202");
        context.AddRange(budget, version, firstAccount, secondAccount);
        await context.SaveChangesAsync();
        return (version.Id, firstAccount.Id, secondAccount.Id);
    }

    private static GlAccount NewExpenseAccount(string code) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = $"Expense {code}",
        Type = AccountType.Expense,
        NormalSide = AccountSide.Debit,
        IsLeaf = true,
        IsActive = true,
    };

    private CP6Context NewContext()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseSqlServer(_connectionString!)
            .Options;
        return new CP6Context(options);
    }

    public void Dispose()
    {
        if (_connectionString == null) return;
        try
        {
            using var context = NewContext();
            context.Database.EnsureDeleted();
        }
        catch
        {
            // Cleanup must not hide the concurrency assertion result.
        }
    }
}
