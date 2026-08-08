using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CP6.Tests.Oa;

public class OaP0ModelTests
{
    private static CP6Context CreateContext()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=cp6-oa-p0-model;Trusted_Connection=True")
            .Options;
        return new CP6Context(options);
    }

    [Fact]
    public void P0_tables_and_rowversions_are_mapped()
    {
        using var db = CreateContext();
        var model = db.Model;

        Assert.Equal("Wf_FlowDefVersion", model.FindEntityType(typeof(Wf_FlowDefVersion))!.GetTableName());
        Assert.Equal("Wf_FormDefVersion", model.FindEntityType(typeof(Wf_FormDefVersion))!.GetTableName());
        Assert.Equal("Wf_FlowDefVersionDependency", model.FindEntityType(typeof(Wf_FlowDefVersionDependency))!.GetTableName());
        Assert.Equal("Wf_FormFlowBinding", model.FindEntityType(typeof(Wf_FormFlowBinding))!.GetTableName());
        Assert.Equal("Wf_FormDraft", model.FindEntityType(typeof(Wf_FormDraft))!.GetTableName());

        AssertRowVersion<Wf_FlowDefVersion>(model);
        AssertRowVersion<Wf_FormDefVersion>(model);
        AssertRowVersion<Wf_FormFlowBinding>(model);
        AssertRowVersion<Wf_FormDraft>(model);
        AssertRowVersion<Wf_FormData>(model);
    }

    [Theory]
    [InlineData(typeof(Wf_FlowDefVersion), "UX_Wf_FlowDefVersion_OneDraft", "[Status] = 0")]
    [InlineData(typeof(Wf_FormDefVersion), "UX_Wf_FormDefVersion_OneDraft", "[Status] = 0")]
    [InlineData(typeof(Wf_FormFlowBinding), "UX_Wf_FormFlowBinding_Active", "[Enable] = 1")]
    [InlineData(typeof(Wf_FormData), "UX_Wf_FormData_SubmissionKey", "[SubmissionKey] IS NOT NULL")]
    [InlineData(typeof(Wf_FlowInstance), "UX_Wf_FlowInstance_ActiveBusiness", "[BizType] IS NOT NULL AND [BizId] IS NOT NULL AND [Status] IN (0, 4)")]
    public void P0_filtered_unique_indexes_are_exact(Type entityType, string indexName, string expectedFilter)
    {
        using var db = CreateContext();
        var index = db.Model.FindEntityType(entityType)!.GetIndexes()
            .Single(x => x.GetDatabaseName() == indexName);

        Assert.True(index.IsUnique);
        Assert.Equal(expectedFilter, index.GetFilter());
    }

    [Fact]
    public void Participant_indexes_are_tenant_leading_and_concrete()
    {
        using var db = CreateContext();
        AssertIndex<Wf_FlowFormTo>(db, "IX_Wf_FlowFormTo_ExpectedParticipant",
            "TenantId", "ExpectedHandlerId", "InstanceId");
        AssertIndex<Wf_FlowFormTo>(db, "IX_Wf_FlowFormTo_ActualParticipant",
            "TenantId", "ActualHandlerId", "InstanceId");
        AssertIndex<Wf_FlowFormTo>(db, "IX_Wf_FlowFormTo_OnBehalfParticipant",
            "TenantId", "OnBehalfOfId", "InstanceId");
        AssertIndex<Wf_FlowCc>(db, "IX_Wf_FlowCc_Participant",
            "TenantId", "RecipientId", "InstanceId");
        AssertIndex<Wf_FlowTask>(db, "IX_Wf_FlowTask_PendingPage",
            "TenantId", "AssigneeId", "Status", "InstanceId", "CreateDate");
    }

    [Fact]
    public void P0_entities_keep_tenant_query_filters()
    {
        using var db = CreateContext();
        foreach (var type in new[]
                 {
                     typeof(Wf_FlowDefVersion), typeof(Wf_FormDefVersion),
                     typeof(Wf_FlowDefVersionDependency), typeof(Wf_FormFlowBinding),
                     typeof(Wf_FormDraft)
                 })
            Assert.NotNull(db.Model.FindEntityType(type)!.GetQueryFilter());
    }

    private static void AssertRowVersion<TEntity>(IModel model)
    {
        var property = model.FindEntityType(typeof(TEntity))!.FindProperty("RowVersion")!;
        Assert.True(property.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
    }

    private static void AssertIndex<TEntity>(CP6Context db, string name, params string[] propertyNames)
    {
        var index = db.Model.FindEntityType(typeof(TEntity))!.GetIndexes()
            .Single(x => x.GetDatabaseName() == name);
        Assert.Equal(propertyNames, index.Properties.Select(x => x.Name));
    }
}
