using System.Security.Cryptography;
using System.Globalization;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DTOs.Mes;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace CP6.Core.Services.Mes;

/// <summary>Evidence for the existing legacy formula; never an OWNER_EFFECTIVE_V1 performance publication.</summary>
internal static class OeeEvidenceV1
{
    internal const string Profile = "LEGACY_CALC_V1", Version = "Main.Oee.InputEvidence.v1";
    // PostgreSQL numeric typmods pad scale on materialization. Evidence compares lossless numeric values,
    // not the incidental CLR decimal scale before/after a database round trip.
    private static string Number(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
    internal static byte[] Inputs(Machine machine, DateTime date, IReadOnlyList<MachineDowntime> downtime,
        IReadOnlyList<ProductionResult> production) => JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version, Profile, date,
            machine = new { machine.Id, machine.TenantId, machine.MachineCd, machine.MachineName,
                machine.RowVersion, machine.PlannedRunMinutesPerDay, CapacityPerHour = machine.CapacityPerHour is decimal capacity ? Number(capacity) : null },
            downtime = downtime.OrderBy(d => d.Id).Select(d => new { d.Id, d.RowVersion, d.WorkOrderNo, d.StartTime, d.EndTime }),
            production = production.OrderBy(p => p.Id).Select(p => new { p.Id, p.RowVersion, p.WorkOrderNo, p.CreateDate, GoodQty = Number(p.GoodQty), DefectQty = Number(p.DefectQty) })
        });

    internal static byte[] Result(OeeDaily row) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        Version, Profile, row.Id, row.TenantId, row.MachineCd, row.OeeDate,
        row.PlannedRunMinutes, row.ActualRunMinutes, row.DowntimeMinutes,
        GoodQty = Number(row.GoodQty), DefectQty = Number(row.DefectQty), Availability = Number(row.Availability),
        Performance = Number(row.Performance), Quality = Number(row.Quality), Oee = Number(row.Oee), row.Remarks
    });

    private static void RequireTransaction(CP6Context db)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction == null || db.CurrentTenantId == Guid.Empty)
            throw new OrderPriceCorrectionException("OEE_EVIDENCE_TRANSACTION_UNAVAILABLE", 503);
    }

    internal static async Task Save(CP6Context db, Machine machine, OeeDaily daily, byte[] input, DateTime observed,
        Guid? actor, string actorName, CancellationToken ct)
    {
        RequireTransaction(db);
        var result = Result(daily); var inputHash = SHA256.HashData(input); var resultHash = SHA256.HashData(result);
        var kind = actor.HasValue ? "HUMAN" : "OEE_WORKER_V1";
        var observation = new NpgsqlParameter("oee_observed", NpgsqlDbType.Timestamp) { Value = observed };
        var actorParameter = new NpgsqlParameter("oee_actor", NpgsqlDbType.Uuid) { Value = (object?)actor ?? DBNull.Value };
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_oee_legacy_v1.calculations
              (id,tenant_id,daily_id,machine_id,profile,evidence_version,input_bytes,input_hash,result_bytes,result_hash,
               daily_version,observed_at,actor_kind,actor_id,actor_name)
            VALUES({Guid.NewGuid()},{db.CurrentTenantId},{daily.Id},{machine.Id},{Profile},{Version},{input},{inputHash},
              {result},{resultHash},{daily.RowVersion!},{observation},{kind},{actorParameter},{actorName})
            """, ct);
    }

    internal static async Task<bool> Matches(CP6Context db, Machine machine, OeeDaily daily, byte[] input, CancellationToken ct)
    {
        RequireTransaction(db);
        var result = Result(daily); var inputHash = SHA256.HashData(input); var resultHash = SHA256.HashData(result);
        return await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT FROM cp6_oee_legacy_v1.calculations
              WHERE tenant_id={db.CurrentTenantId} AND daily_id={daily.Id} AND machine_id={machine.Id}
                AND profile={Profile} AND evidence_version={Version} AND daily_version={daily.RowVersion!}
                AND input_hash={inputHash} AND input_bytes={input} AND result_hash={resultHash} AND result_bytes={result}) AS "Value"
            """).SingleAsync(ct);
    }
}
