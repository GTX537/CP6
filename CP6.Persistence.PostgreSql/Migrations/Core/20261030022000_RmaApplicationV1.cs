using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261030022000_RmaApplicationV1")]
public sealed class RmaApplicationV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_rma_application_v1;
        REVOKE ALL ON SCHEMA cp6_rma_application_v1 FROM PUBLIC;
        CREATE TABLE cp6_rma_application_v1.heads (
          tenant_id uuid NOT NULL, id uuid NOT NULL, actor_id uuid NOT NULL, outbound_id uuid NOT NULL,
          version integer NOT NULL CHECK(version>0), PRIMARY KEY(tenant_id,id)
        );
        CREATE TABLE cp6_rma_application_v1.versions (
          tenant_id uuid NOT NULL, id uuid NOT NULL, version integer NOT NULL CHECK(version>0),
          state text NOT NULL CHECK(state IN ('DRAFT','SUBMITTED')), payload jsonb NOT NULL,
          PRIMARY KEY(tenant_id,id,version),
          FOREIGN KEY(tenant_id,id) REFERENCES cp6_rma_application_v1.heads(tenant_id,id)
        );
        ALTER TABLE cp6_rma_application_v1.heads ADD CONSTRAINT rma_application_head_version
          FOREIGN KEY(tenant_id,id,version) REFERENCES cp6_rma_application_v1.versions(tenant_id,id,version)
          DEFERRABLE INITIALLY DEFERRED;
        CREATE FUNCTION cp6_rma_application_v1.immutable() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'RMA_APPLICATION_VERSION_IMMUTABLE' USING ERRCODE='23514'; END $$;
        CREATE TRIGGER rma_application_immutable BEFORE UPDATE OR DELETE ON cp6_rma_application_v1.versions
          FOR EACH ROW EXECUTE FUNCTION cp6_rma_application_v1.immutable();
        CREATE TRIGGER rma_application_no_truncate BEFORE TRUNCATE ON cp6_rma_application_v1.versions
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_application_v1.immutable();
        """);
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("RMA application history requires forward-only migration.");
}
