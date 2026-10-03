namespace CP6.Persistence.PostgreSql;

// Frozen from the v1 baseline CreateTable operations; never derive old DDL from a future model.
internal static class PostgreSqlErpIntegrationTokenTablesV1
{
    public static readonly PostgreSqlTokenTableV1[] All =
    [
        new("erp_integration", "Aggregate", "RowVersion"),
        new("erp_integration", "CommandInbox", "RowVersion"),
        new("erp_integration", "Cp6_DeadLetterRecord", "RowVersion"),
        new("erp_integration", "Cp6_OutboxMessage", "RowVersion"),
        new("erp_integration", "OrderBridgeDispatch", "RowVersion"),
        new("erp_integration", "Request", "RowVersion"),
    ];
}
