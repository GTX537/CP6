using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261109014500_LocationWithdrawalJournalDispatchV1")]
public sealed class LocationWithdrawalJournalDispatchV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE OR REPLACE FUNCTION cp6_location_rule_review_control_v1.command_complete() RETURNS trigger
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          -- PostgreSQL resolves relations in a SQL expression before its boolean filters.
          -- This shared journal trigger must route other Owners before touching the private table.
          IF NEW.owner<>'Main.Wms.LocationRuleWithdrawal.operations.v1' OR NEW.state<>'COMMITTED' THEN
            RETURN NULL;
          END IF;
          IF convert_from(NEW.result_bytes,'UTF8')::jsonb->>'State'='WITHDRAWN' AND NOT EXISTS(
            SELECT FROM cp6_location_rule_review_control_v1.withdrawals w WHERE w.tenant_id=NEW.tenant_id
              AND w.operation_id=NEW.operation_id AND w.actor_id=NEW.actor_id AND w.journal=NEW.owner
              AND NEW.contract='Main.Wms.LocationRuleWithdrawal.commands.v1' AND NEW.object_ids=ARRAY[w.location_rule_id]
              AND w.command_id::text=convert_from(NEW.result_bytes,'UTF8')::jsonb->>'TransportCommandId'
              AND w.xmin=pg_current_xact_id()::text::xid)
          THEN RAISE EXCEPTION 'LOCATION_RULE_WITHDRAWAL_ATOMIC_FACT_REQUIRED'; END IF;
          RETURN NULL;
        END $$;
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Shared journal trigger dispatch requires forward recovery.");
}
