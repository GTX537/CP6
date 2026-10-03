namespace CP6.Persistence.PostgreSql;

// Frozen from the v1 baseline CreateTable operations; never derive old DDL from a future model.
internal static class PostgreSqlIdentityPriorityTokenTablesV1
{
    public static readonly PostgreSqlTokenTableV1[] All =
    [
        new("crm_identity_priority", "Cp6_DeadLetterRecord", "RowVersion"),
        new("crm_identity_priority", "Cp6_InboxAggregateCheckpoint", "RowVersion"),
        new("crm_identity_priority", "Cp6_InboxMessage", "RowVersion"),
        new("crm_identity_priority", "Cp6_OutboxMessage", "RowVersion"),
    ];
}
