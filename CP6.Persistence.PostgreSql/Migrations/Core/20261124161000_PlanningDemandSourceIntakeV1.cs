using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261124161000_PlanningDemandSourceIntakeV1")]
public sealed class PlanningDemandSourceIntakeV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_planning_demand_source_v1;
        CREATE TABLE cp6_planning_demand_source_v1.lineages (
            tenant_id uuid NOT NULL,
            producer text COLLATE "C" NOT NULL CHECK (producer='Main.Sales'),
            lineage_id text COLLATE "C" NOT NULL,
            order_id uuid NOT NULL,
            line_id uuid NOT NULL,
            schedule_id text COLLATE "C" NOT NULL,
            PRIMARY KEY (tenant_id,producer,lineage_id)
        );
        CREATE TABLE cp6_planning_demand_source_v1.inbox (
            tenant_id uuid NOT NULL,
            producer text COLLATE "C" NOT NULL,
            operation_key text COLLATE "C" NOT NULL,
            lineage_id text COLLATE "C" NOT NULL,
            source_version bigint NOT NULL CHECK (source_version>0),
            source_digest text COLLATE "C" NOT NULL CHECK (source_digest ~ '^[0-9a-f]{64}$'),
            body_sha256 text COLLATE "C" NOT NULL CHECK (body_sha256 ~ '^[0-9a-f]{64}$'),
            result_owner text COLLATE "C" NOT NULL DEFAULT 'Main.Planning' CHECK (result_owner='Main.Planning'),
            result_id uuid NOT NULL,
            sales_receipt bytea NOT NULL,
            sales_envelope bytea NOT NULL,
            PRIMARY KEY (tenant_id,producer,operation_key),
            UNIQUE (tenant_id,producer,lineage_id,source_version),
            UNIQUE (tenant_id,result_id),
            FOREIGN KEY (tenant_id,producer,lineage_id) REFERENCES cp6_planning_demand_source_v1.lineages,
            FOREIGN KEY (tenant_id,result_owner,result_id) REFERENCES cp6_main_owner_v1.operations
        );
        CREATE TABLE cp6_planning_demand_source_v1.demands (
            tenant_id uuid NOT NULL,
            demand_id uuid NOT NULL,
            producer text COLLATE "C" NOT NULL,
            lineage_id text COLLATE "C" NOT NULL,
            PRIMARY KEY (tenant_id,demand_id),
            UNIQUE (tenant_id,producer,lineage_id),
            UNIQUE (tenant_id,producer,lineage_id,demand_id),
            FOREIGN KEY (tenant_id,producer,lineage_id) REFERENCES cp6_planning_demand_source_v1.lineages
        );
        CREATE TABLE cp6_planning_demand_source_v1.demand_versions (
            tenant_id uuid NOT NULL,
            demand_id uuid NOT NULL,
            demand_version bigint NOT NULL CHECK (demand_version>0),
            target bytea NOT NULL,
            accepted_at timestamptz NOT NULL,
            PRIMARY KEY (tenant_id,demand_id,demand_version),
            FOREIGN KEY (tenant_id,demand_id) REFERENCES cp6_planning_demand_source_v1.demands
        );
        CREATE TABLE cp6_planning_demand_source_v1.source_bindings (
            tenant_id uuid NOT NULL,
            producer text COLLATE "C" NOT NULL,
            lineage_id text COLLATE "C" NOT NULL,
            source_version bigint NOT NULL,
            demand_id uuid NOT NULL,
            demand_version bigint NOT NULL,
            order_version text COLLATE "C" NOT NULL,
            release_id text COLLATE "C" NOT NULL,
            release_line_id text COLLATE "C" NOT NULL,
            PRIMARY KEY (tenant_id,producer,lineage_id,source_version),
            UNIQUE (tenant_id,demand_id,demand_version),
            FOREIGN KEY (tenant_id,producer,lineage_id,source_version)
                REFERENCES cp6_planning_demand_source_v1.inbox (tenant_id,producer,lineage_id,source_version),
            FOREIGN KEY (tenant_id,producer,lineage_id,demand_id)
                REFERENCES cp6_planning_demand_source_v1.demands (tenant_id,producer,lineage_id,demand_id),
            FOREIGN KEY (tenant_id,demand_id,demand_version) REFERENCES cp6_planning_demand_source_v1.demand_versions
        );
        CREATE TABLE cp6_planning_demand_source_v1.controls (
            tenant_id uuid NOT NULL,
            producer text COLLATE "C" NOT NULL,
            lineage_id text COLLATE "C" NOT NULL,
            source_version bigint NOT NULL,
            position integer NOT NULL CHECK (position>=0),
            order_version text COLLATE "C" NOT NULL,
            effective_at_ticks bigint NOT NULL CHECK (effective_at_ticks>=0),
            disposition text COLLATE "C" NOT NULL CHECK (disposition IN ('BLOCK','TERMINATE')),
            plans boolean NOT NULL,
            fact bytea NOT NULL,
            PRIMARY KEY (tenant_id,producer,lineage_id,source_version,position),
            FOREIGN KEY (tenant_id,producer,lineage_id,source_version)
                REFERENCES cp6_planning_demand_source_v1.inbox (tenant_id,producer,lineage_id,source_version)
        );

        CREATE FUNCTION cp6_planning_demand_source_v1.immutable() RETURNS trigger
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN RAISE EXCEPTION 'PLANNING_DEMAND_SOURCE_ORIGINAL_IMMUTABLE' USING ERRCODE='23514'; END $$;
        DO $$ DECLARE table_name text; BEGIN
            FOREACH table_name IN ARRAY ARRAY['lineages','inbox','demands','demand_versions','source_bindings','controls'] LOOP
                EXECUTE format('CREATE TRIGGER original_immutable BEFORE UPDATE OR DELETE OR TRUNCATE
                    ON cp6_planning_demand_source_v1.%I FOR EACH STATEMENT
                    EXECUTE FUNCTION cp6_planning_demand_source_v1.immutable()',table_name);
            END LOOP;
        END $$;
        REVOKE ALL ON SCHEMA cp6_planning_demand_source_v1 FROM PUBLIC;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_planning_demand_source_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_planning_demand_source_v1 FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Planning source originals and receipt recovery require forward recovery.");
}
