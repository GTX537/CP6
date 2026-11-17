using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Fin.FinanceCostCommandCanonicalV1;
using static CP6.Core.Services.Fin.ManufacturingFinanceScopeV1;

namespace CP6.Core.Services.Fin;

internal sealed class FinanceCostCommandJournalV1(CP6Context db)
{
    internal const string Commands = "api/fin/cost/v1/commands";
    private const string Owner = "Finance Costing";
    private Guid Tenant => db.CurrentTenantId;

    internal Task<Binding?> FindAsync(Guid ledger, Guid command, CancellationToken ct)
        => db.Database.SqlQuery<Binding>($"""
            SELECT original_principal AS "Principal",method AS "Method",canonical_route AS "Route",
                body_digest AS "BodyDigest",expected_digest AS "ExpectedDigest",accepted_at AS "AcceptedAt"
            FROM cp6_fin_mfg_v1.finance_command_bindings
            WHERE (tenant_id,ledger_id,command_id)=({Tenant},{ledger},{command})
            """).SingleOrDefaultAsync(ct);

    internal async Task BindAsync(Guid ledger, Guid actor, StandardDocumentBinding binding, CancellationToken ct)
    {
        if (await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_mfg_v1.finance_command_bindings
                (tenant_id,ledger_id,command_id,original_principal,method,canonical_route,body_digest,expected_digest,accepted_at)
            VALUES ({Tenant},{ledger},{binding.CommandId},{actor.ToString("D")},'POST',{binding.Route},
                {binding.R2BodyDigest},{binding.R2ExpectedDigest},date_trunc('milliseconds',clock_timestamp()))
            ON CONFLICT (tenant_id,ledger_id,command_id) DO NOTHING
            """, ct) != 1)
            throw Error("COMMAND_IDENTITY_CONFLICT", 409);
    }

    internal async Task RecordAsync(Guid ledger, StandardDocumentBinding binding, StandardDocumentReplyV1 native,
        CancellationToken ct)
    {
        var projection = Bytes(new
        {
            schema = "CP6-STANDARD-DOCUMENT-COMMAND-RESULT-1",
            native.CommandId, native.DocumentId, native.Version, native.PlanId,
            expiresAt = native.ExpiresAt is { } expires ? Time(expires) : null,
        });
        var receipt = Bytes(new
        {
            schema = "COST-COMMAND-1",
            id = native.ReceiptId,
            native.CommandId,
            operation = native.Operation switch
            {
                "CREATE" => "CREATE_STANDARD_DOCUMENT",
                "PREPARE" => "PREPARE_STANDARD_COLLECTION",
                "FREEZE" => "FREEZE_STANDARD_DOCUMENT",
                _ => throw new InvalidOperationException("标准成本单据命令未知。"),
            },
            businessKey = native.DocumentId,
            bodyDigest = Hex(binding.R2BodyDigest),
            resultRef = Reference(native.CommandId, "STANDARD_DOCUMENT_COMMAND_RESULT", projection),
            outcome = "APPLIED",
            problem = (object?)null,
            recordedAt = Time(DateTimeOffset.UtcNow),
            uowId = db.Database.CurrentTransaction!.TransactionId,
        });

        if (await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_mfg_v1.finance_command_receipts (tenant_id,ledger_id,command_id,receipt,result)
            VALUES ({Tenant},{ledger},{binding.CommandId},{receipt},{projection})
            """, ct) != 1)
            throw Error("AUTHORITY_UNAVAILABLE", 503);
    }

    internal Task<Terminal?> ReadTerminalAsync(Guid ledger, Guid command, CancellationToken ct)
        => db.Database.SqlQuery<Terminal>($"""
            SELECT receipt AS "Receipt",result AS "Result" FROM cp6_fin_mfg_v1.finance_command_receipts
            WHERE (tenant_id,ledger_id,command_id)=({Tenant},{ledger},{command})
            """).SingleOrDefaultAsync(ct);

    internal static object Found(Guid receiptId, byte[] receipt) => new
    {
        kind = "FOUND",
        reply = new
        {
            kind = "COMMITTED",
            receiptRef = Reference(receiptId, "COMMAND_RECEIPT", receipt),
            receipt = JsonBody(receipt),
            replayed = false,
        },
    };

    internal static JsonElement JsonBody(byte[] bytes)
    {
        using var json = JsonDocument.Parse(bytes);
        return json.RootElement.Clone();
    }

    private static object Reference(Guid id, string purpose, byte[] body)
        => new { owner = Owner, id, version = "1", digest = Hex(Hash(body)), purpose };

    internal sealed class Binding
    {
        public string Principal { get; set; } = null!;
        public string Method { get; set; } = null!;
        public string Route { get; set; } = null!;
        public byte[] BodyDigest { get; set; } = null!;
        public byte[] ExpectedDigest { get; set; } = null!;
        public DateTimeOffset? AcceptedAt { get; set; }

        internal bool Matches(Guid actor, StandardDocumentBinding binding)
            => Principal == actor.ToString("D") && Method == "POST" && Route == binding.Route
                && BodyDigest.AsSpan().SequenceEqual(AcceptedAt.HasValue ? binding.R2BodyDigest : binding.BodyDigest)
                && ExpectedDigest.AsSpan().SequenceEqual(AcceptedAt.HasValue ? binding.R2ExpectedDigest : binding.ExpectedDigest);
    }

    internal sealed class Terminal
    {
        public byte[] Receipt { get; set; } = null!;
        public byte[] Result { get; set; } = null!;
    }
}
