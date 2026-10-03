namespace CP6.Core.Persistence;

/// <summary>Forward SQL Server migration for an opaque, durable tenant generation.</summary>
public static class IdentityGenerationSqlServerV1
{
    public const string SeedSql = """
        INSERT INTO dbo.CrmIdentityTenantGenerations(TenantId, Generation)
        SELECT DISTINCT TenantId, CONVERT(bigint,1) FROM crm_identity.Snapshot;
        """;

    public const string TriggerSql = """
        CREATE TRIGGER crm_identity.trg_CrmIdentitySnapshot_TenantGeneration
        ON crm_identity.Snapshot AFTER INSERT, UPDATE, DELETE AS
        BEGIN
          SET NOCOUNT ON;
          DECLARE @tenants TABLE (TenantId uniqueidentifier NOT NULL PRIMARY KEY);
          INSERT INTO @tenants(TenantId)
          SELECT TenantId FROM inserted UNION SELECT TenantId FROM deleted;
          MERGE dbo.CrmIdentityTenantGenerations WITH (HOLDLOCK) AS target
          USING @tenants AS source ON target.TenantId=source.TenantId
          WHEN MATCHED THEN UPDATE SET Generation=target.Generation+CONVERT(bigint,1)
          WHEN NOT MATCHED THEN INSERT(TenantId, Generation) VALUES(source.TenantId,CONVERT(bigint,1));
        END;
        """;
}
