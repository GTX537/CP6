using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261030022900_RmaAuthorizationV1")]
public sealed class RmaAuthorizationV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE cp6_rma_formal_v1.entitlement_heads(tenant_id uuid NOT NULL,coordinate_key text NOT NULL,
          revision integer NOT NULL CHECK(revision>0),source_signature text NOT NULL,reference bytea NOT NULL,
          PRIMARY KEY(tenant_id,coordinate_key));
        CREATE TABLE cp6_rma_formal_v1.authorization_heads(tenant_id uuid NOT NULL,application_id uuid NOT NULL,
          decision_id uuid NOT NULL,version integer NOT NULL CHECK(version>0),PRIMARY KEY(tenant_id,application_id),
          UNIQUE(tenant_id,decision_id),FOREIGN KEY(tenant_id,application_id) REFERENCES cp6_rma_formal_v1.heads(tenant_id,id));
        CREATE TABLE cp6_rma_formal_v1.authorization_versions(tenant_id uuid NOT NULL,decision_id uuid NOT NULL,
          version integer NOT NULL CHECK(version>0),application_id uuid NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,decision_id,version),FOREIGN KEY(tenant_id,application_id) REFERENCES cp6_rma_formal_v1.heads(tenant_id,id));
        ALTER TABLE cp6_rma_formal_v1.authorization_heads ADD FOREIGN KEY(tenant_id,decision_id,version)
          REFERENCES cp6_rma_formal_v1.authorization_versions(tenant_id,decision_id,version) DEFERRABLE INITIALLY DEFERRED;
        CREATE TABLE cp6_rma_formal_v1.return_claims(tenant_id uuid NOT NULL,claim_id uuid NOT NULL,coordinate_key text NOT NULL,
          application_id uuid NOT NULL,decision_id uuid NOT NULL,body bytea NOT NULL,PRIMARY KEY(tenant_id,claim_id),
          FOREIGN KEY(tenant_id,coordinate_key) REFERENCES cp6_rma_formal_v1.entitlement_heads(tenant_id,coordinate_key));
        CREATE TABLE cp6_rma_formal_v1.return_claim_segments(tenant_id uuid NOT NULL,claim_id uuid NOT NULL,ordinal integer NOT NULL,
          coordinate_key text NOT NULL,range_from numeric(21,8) NOT NULL,range_to numeric(21,8) NOT NULL,
          state text NOT NULL CHECK(state IN('ACTIVE_RESERVED','CONSUMED_BY_RECEIPT','UNKNOWN_HOLD','RELEASED_BY_SUCCESSOR','VOID_BY_SOURCE_CORRECTION')),
          PRIMARY KEY(tenant_id,claim_id,ordinal),CHECK(range_from>=0 AND range_to>range_from),
          FOREIGN KEY(tenant_id,claim_id) REFERENCES cp6_rma_formal_v1.return_claims(tenant_id,claim_id));
        CREATE INDEX return_claim_coordinate ON cp6_rma_formal_v1.return_claim_segments(tenant_id,coordinate_key,state);
        CREATE TABLE cp6_rma_formal_v1.return_shares(tenant_id uuid NOT NULL,share_id uuid NOT NULL,application_id uuid NOT NULL,
          decision_id uuid NOT NULL,claim_id uuid NOT NULL,body bytea NOT NULL,PRIMARY KEY(tenant_id,share_id),
          FOREIGN KEY(tenant_id,claim_id) REFERENCES cp6_rma_formal_v1.return_claims(tenant_id,claim_id));
        CREATE TABLE cp6_rma_formal_v1.authorization_outbox(tenant_id uuid NOT NULL,event_id uuid NOT NULL,
          decision_id uuid NOT NULL,version integer NOT NULL,body bytea NOT NULL,PRIMARY KEY(tenant_id,event_id),
          UNIQUE(tenant_id,decision_id,version),FOREIGN KEY(tenant_id,decision_id,version)
            REFERENCES cp6_rma_formal_v1.authorization_versions(tenant_id,decision_id,version));
        DO $body$ DECLARE name text; BEGIN
          FOREACH name IN ARRAY ARRAY['authorization_versions','return_claims','return_shares','authorization_outbox'] LOOP
            EXECUTE format('CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON cp6_rma_formal_v1.%I FOR EACH ROW EXECUTE FUNCTION cp6_rma_formal_v1.immutable()',name);
            EXECUTE format('CREATE TRIGGER no_truncate BEFORE TRUNCATE ON cp6_rma_formal_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable()',name);
          END LOOP;
        END $body$;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,a.sort,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        CROSS JOIN (VALUES ('authorization-decide','决定退货授权',28),('authorization-revoke','撤销退货授权',29)) a(code,name,sort)
        WHERE m."MenuKey"='wms-rma' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x
          WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Return authorization and claim history require forward-only migration.");
}
