using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace CP6.ErpLive.Fixture;

internal static partial class ErpLiveFixture
{
    public static async Task SnapshotAsync(string fixturePath, string outputPath, CancellationToken ct)
    {
        var (fixture, owner) = await ReadFixtureAsync(fixturePath, ct);
        var output = Path.GetFullPath(outputPath);
        // Never replace the private credential/ownership inputs with public evidence.
        if (new[] { "ownership.json", "live-fixture.json", "appsettings.Local.json" }
            .Contains(Path.GetFileName(output), StringComparer.OrdinalIgnoreCase))
            throw new FixtureException("C03_SNAPSHOT_OUTPUT_COLLIDES_WITH_PRIVATE_INPUT");
        var canonical = await ReadCanonicalAsync(OwnedConnection(owner), owner.TenantIds, ct);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await WriteJsonAsync(output, new
        {
            schemaId = "cp6.c03.erp-live-snapshot.v1", observedAtUtc = DateTimeOffset.UtcNow,
            authority = fixture.Authority, tenantIds = owner.TenantIds,
            scope = "ERP request/result transport; post-commit WMS/MES bridges are observable but not executed by this fixture",
            canonical
        }, ct);
        Console.WriteLine("C03 public-safe canonical ERP snapshot written.");
    }

    // Explicit column allowlists exclude legal/customer/staff names, addresses, tokens, payloads and connection details.
    // One SQL snapshot transaction supplies a consistent view while the real request/dispatch workers continue running.
    private static async Task<Dictionary<string, object>> ReadCanonicalAsync(string connection, Guid[] tenants, CancellationToken ct)
    {
        await using var db = new SqlConnection(connection);
        await db.OpenAsync(ct);
        await using var transaction = await db.BeginTransactionAsync(IsolationLevel.Snapshot, ct);
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var query in CanonicalQueries)
        {
            var rows = await db.QueryAsync(new CommandDefinition(query.Sql, new { tenants }, transaction,
                commandTimeout: 60, cancellationToken: ct));
            result.Add(query.Name, rows.Select(row => (IDictionary<string, object>)row)
                .Select(row => row.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)).ToArray());
        }
        await transaction.CommitAsync(ct);
        return result;
    }

    private static readonly (string Name, string Sql)[] CanonicalQueries =
    [
        ("tenants", """
            SELECT Id AS id, Enable AS enabled, ExpireDate AS expiresAtUtc
            FROM dbo.Sys_Tenants WHERE Id IN @tenants ORDER BY Id
            """),
        ("businessPartners", """
            SELECT TenantId AS tenantId, Id AS id, BpCd AS [key], CrmAccountId AS accountId,
                Status AS status, CustomerFlg AS customer, IsFrozen AS frozen, CurrencyCd AS currency,
                IsDeleted AS deleted, RowVersion AS rowVersion
            FROM dbo.T_WebBusinessPartner WHERE TenantId IN @tenants ORDER BY TenantId, BpCd
            """),
        ("quotations", """
            SELECT TenantId AS tenantId, Id AS id, QtnNo AS [key], CustomerCd AS businessPartnerKey,
                EstimateCheckFlg AS approvalStatus, TotalAmount AS totalAmount, CurrencyCd AS currency,
                ValidUntilUtc AS validUntilUtc, CustomerAcceptedAtUtc AS acceptedAtUtc,
                AcceptedContentSha256 AS acceptedContentSha256, OrderType AS orderType,
                OrderDeliveryDate AS orderDeliveryDate, IsDeleted AS deleted, RowVersion AS rowVersion
            FROM dbo.T_Quotation WHERE TenantId IN @tenants ORDER BY TenantId, QtnNo
            """),
        ("quotationLines", """
            SELECT TenantId AS tenantId, Id AS id, QtnNo AS quotationKey, DetailNo AS detailNo,
                Quantity AS quantity, UnitPrice AS unitPrice, Unit AS unit, Amount AS amount, IsDeleted AS deleted, RowVersion AS rowVersion
            FROM dbo.T_QuotationDetail WHERE TenantId IN @tenants ORDER BY TenantId, QtnNo, DetailNo
            """),
        ("products", """
            SELECT TenantId AS tenantId, Id AS id, ProductCd AS [key], CustomerCd AS businessPartnerKey,
                QuotationNo AS quotationKey, Branch1 AS branch, Status AS status, WfApprovalFlg AS workflowApproved,
                SalesPriceDiv AS priceMode, QtyUnit AS quantityUnit, UnitPriceUnit AS priceUnit, IsDeleted AS deleted, RowVersion AS rowVersion
            FROM dbo.T_ProductMaster WHERE TenantId IN @tenants ORDER BY TenantId, ProductCd
            """),
        ("inbox", """
            SELECT TenantId AS tenantId, MessageId AS messageId, EventType AS eventType, AggregateId AS aggregateId,
                AggregateVersion AS aggregateVersion, PayloadSha256 AS payloadSha256, Status AS status,
                AttemptCount AS attemptCount, ConflictCount AS conflictCount, LastConflictSha256 AS lastConflictSha256,
                ErrorCode AS errorCode, ReceivedAtUtc AS receivedAtUtc, ProcessedAtUtc AS processedAtUtc,
                RetryAtUtc AS retryAtUtc, ReplayedAtUtc AS replayedAtUtc, ReplayReasonCode AS replayReasonCode,
                RowVersion AS rowVersion
            FROM erp_integration.CommandInbox WHERE TenantId IN @tenants ORDER BY TenantId, MessageId
            """),
        ("requests", """
            SELECT TenantId AS tenantId, Kind AS kind, AggregateId AS aggregateId, RequestVersion AS requestVersion,
                RequestId AS requestId, AccountId AS accountId, InputSha256 AS inputSha256, Terminal AS terminal,
                Succeeded AS succeeded, ResultVersion AS resultVersion, ResultType AS resultType,
                JSON_VALUE(ResultDataJson, '$.orderKey') AS orderKey,
                JSON_VALUE(ResultDataJson, '$.businessPartnerKey') AS businessPartnerKey,
                JSON_VALUE(ResultDataJson, '$.quotationKey') AS quotationKey,
                JSON_VALUE(ResultDataJson, '$.errorCode') AS errorCode,
                JSON_VALUE(ResultDataJson, '$.retryable') AS retryable,
                UpdatedAtUtc AS updatedAtUtc, RowVersion AS rowVersion
            FROM erp_integration.Request WHERE TenantId IN @tenants ORDER BY TenantId, Kind, AggregateId, RequestVersion
            """),
        ("aggregates", """
            SELECT TenantId AS tenantId, Kind AS kind, AggregateId AS aggregateId, LastRequestVersion AS lastRequestVersion,
                LastAggregateVersion AS lastAggregateVersion, LastResultVersion AS lastResultVersion, RowVersion AS rowVersion
            FROM erp_integration.Aggregate WHERE TenantId IN @tenants ORDER BY TenantId, Kind, AggregateId
            """),
        ("outbox", """
            SELECT TenantId AS tenantId, Id AS id, MessageId AS messageId, TopicName AS topicName,
                PartitionKey AS partitionKey, PayloadSha256 AS payloadSha256, CorrelationId AS correlationId,
                CausationId AS causationId, AggregateId AS aggregateId, AggregateVersion AS aggregateVersion,
                Status AS status, AttemptCount AS attemptCount, CreatedAtUtc AS createdAtUtc,
                AvailableAtUtc AS availableAtUtc, PublishedAtUtc AS publishedAtUtc,
                DeadLetteredAtUtc AS deadLetteredAtUtc, LastErrorCode AS lastErrorCode, RowVersion AS rowVersion
            FROM erp_integration.Cp6_OutboxMessage WHERE TenantId IN @tenants ORDER BY TenantId, CreatedAtUtc, MessageId
            """),
        ("orders", """
            SELECT TenantId AS tenantId, Id AS id, WebOrderNo AS [key], CustomerCd AS businessPartnerKey,
                CrmOpportunityId AS opportunityId, CrmAccountId AS accountId, CrmRequestId AS requestId,
                CrmRequestVersion AS requestVersion, CrmQuotationId AS quotationId, CrmRequestSha256 AS requestSha256,
                CurrencyCd AS currency, FxRate AS fxRate, OrderType AS orderType, Quantity AS quantity,
                OrderStatus AS status, IsDeleted AS deleted, RowVersion AS rowVersion
            FROM dbo.T_Order WHERE TenantId IN @tenants ORDER BY TenantId, WebOrderNo
            """),
        ("orderLines", """
            SELECT TenantId AS tenantId, Id AS id, WebOrderNo AS orderKey, WebOrderDetailNo AS detailNo,
                ProductCd AS productKey, Quantity AS quantity, IndividualUnitPrice AS individualUnitPrice,
                SetUnitPrice AS setUnitPrice, SalesPriceDiv AS priceMode, Amount AS amount, QtyUnit AS quantityUnit, UnitPriceUnit AS priceUnit,
                IsDeleted AS deleted, RowVersion AS rowVersion
            FROM dbo.T_OrderDetail WHERE TenantId IN @tenants ORDER BY TenantId, WebOrderNo, WebOrderDetailNo
            """),
        ("orderBridges", """
            SELECT TenantId AS tenantId, OrderKey AS orderKey, AttemptCount AS attemptCount,
                AvailableAtUtc AS availableAtUtc, CompletedAtUtc AS completedAtUtc, LastErrorCode AS lastErrorCode,
                RowVersion AS rowVersion
            FROM erp_integration.OrderBridgeDispatch WHERE TenantId IN @tenants ORDER BY TenantId, OrderKey
            """),
        ("replayAudits", """
            SELECT TenantId AS tenantId, OperationId AS operationId, MessageId AS messageId,
                PayloadSha256 AS payloadSha256, InputRowVersion AS inputRowVersion,
                ReasonCode AS reasonCode, PreviousAttemptCount AS previousAttemptCount, ReplayedAtUtc AS replayedAtUtc
            FROM erp_integration.InboxReplayAudit WHERE TenantId IN @tenants ORDER BY TenantId, ReplayedAtUtc, OperationId
            """),
        ("identitySnapshots", """
            SELECT TenantId AS tenantId, AggregateId AS aggregateId, Version AS version, EventType AS eventType,
                PayloadSha256 AS payloadSha256, IsDeleted AS deleted, UpdatedAtUtc AS updatedAtUtc
            FROM crm_identity.Snapshot WHERE TenantId IN @tenants ORDER BY TenantId, AggregateId
            """),
        ("serviceTokens", """
            SELECT t.Id AS tenantId, COUNT_BIG(s.TenantId) AS tokenCount,
                SUM(CASE WHEN s.RevokedAtUtc IS NOT NULL THEN CONVERT(bigint,1) ELSE CONVERT(bigint,0) END) AS revokedCount,
                SUM(CASE WHEN s.RevokedAtUtc IS NULL AND s.ExpiresAtUtc > SYSUTCDATETIME() THEN CONVERT(bigint,1) ELSE CONVERT(bigint,0) END) AS activeCount
            FROM dbo.Sys_Tenants AS t LEFT JOIN crm_identity.ServiceToken AS s ON s.TenantId=t.Id
            WHERE t.Id IN @tenants GROUP BY t.Id ORDER BY t.Id
            """),
        ("storageFaults", """
            SELECT c.name AS constraintName, TRY_CONVERT(uniqueidentifier, CONVERT(nvarchar(128), target.value)) AS tenantId,
                c.is_disabled AS disabled, c.is_not_trusted AS preservesExistingRows, 547 AS expectedSqlError
            FROM sys.check_constraints AS c
            INNER JOIN sys.extended_properties AS target ON target.class=1 AND target.major_id=c.object_id
                AND target.minor_id=0 AND target.name=N'CP6.C03FixtureFaultTenant'
            WHERE c.name=N'C03Fixture_OrderStorageFault' AND c.parent_object_id=OBJECT_ID(N'dbo.T_Order')
                AND TRY_CONVERT(uniqueidentifier, CONVERT(nvarchar(128), target.value)) IN @tenants
            """)
    ];
}
