using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Fin.FinanceCostCommandCanonicalV1;
using static CP6.Core.Services.Fin.ManufacturingFinanceScopeV1;

namespace CP6.Core.Services.Fin;

internal sealed record FinanceCostCommandIdentityV1(Guid CommandId, string Route, byte[] BodyDigest, byte[] ExpectedDigest)
{
    internal static FinanceCostCommandIdentityV1 Create(Guid id, string route, object request, object expected)
    {
        if (id == Guid.Empty)
            throw Error("FINANCE_COMMAND_ID_INVALID", 422);
        return new(id, route, Digest(request), Digest(expected));
    }
}

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

    internal async Task BindAsync(Guid ledger, Guid actor, FinanceCostCommandIdentityV1 binding, CancellationToken ct)
    {
        if (await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_fin_mfg_v1.finance_command_bindings
                (tenant_id,ledger_id,command_id,original_principal,method,canonical_route,body_digest,expected_digest,accepted_at)
            VALUES ({Tenant},{ledger},{binding.CommandId},{actor.ToString("D")},'POST',{binding.Route},
                {binding.BodyDigest},{binding.ExpectedDigest},date_trunc('milliseconds',clock_timestamp()))
            ON CONFLICT (tenant_id,ledger_id,command_id) DO NOTHING
            """, ct) != 1)
            throw Error("COMMAND_IDENTITY_CONFLICT", 409);
    }

    internal async Task RecordAsync(Guid ledger, FinanceCostCommandIdentityV1 binding, Guid receiptId,
        string operation, Guid businessKey, string purpose, object result, CancellationToken ct)
    {
        var projection = Bytes(result);
        var receipt = Bytes(new
        {
            schema = "COST-COMMAND-1",
            id = receiptId,
            binding.CommandId,
            operation,
            businessKey,
            bodyDigest = Hex(binding.BodyDigest),
            resultRef = Reference(binding.CommandId, purpose, projection),
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

    internal static object Found(byte[] bytes)
    {
        var receipt = JsonBody(bytes);
        return new
        {
            kind = "FOUND",
            reply = new
            {
                kind = "COMMITTED",
                receiptRef = Reference(receipt.GetProperty("id").GetGuid(), "COMMAND_RECEIPT", bytes),
                receipt,
                replayed = false,
            },
        };
    }

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

        internal bool Matches(Guid actor, FinanceCostCommandIdentityV1 binding)
            => Principal == actor.ToString("D") && Method == "POST" && Route == binding.Route
                && BodyDigest.AsSpan().SequenceEqual(binding.BodyDigest)
                && ExpectedDigest.AsSpan().SequenceEqual(binding.ExpectedDigest);
    }

    internal sealed class Terminal
    {
        public byte[] Receipt { get; set; } = null!;
        public byte[] Result { get; set; } = null!;
    }
}
