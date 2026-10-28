using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261030015000_CapacityOccupancyV1")]
public sealed class CapacityOccupancyV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_capacity_v1;
        REVOKE ALL ON SCHEMA cp6_capacity_v1 FROM PUBLIC;
        CREATE TABLE cp6_capacity_v1.domains(
          environment_id uuid NOT NULL,sandbox_id uuid NOT NULL,tenant_id uuid NOT NULL,domain_key text NOT NULL,
          identity_bytes bytea NOT NULL,identity_digest text NOT NULL CHECK(identity_digest=encode(sha256(identity_bytes),'hex')),
          revision bigint NOT NULL CHECK(revision>=0),physical numeric(21,8) NOT NULL CHECK(physical>=0),unreceived numeric(21,8) NOT NULL CHECK(unreceived>=0),
          CHECK(physical+unreceived<=9999999999999.99999999),PRIMARY KEY(environment_id,sandbox_id,tenant_id,domain_key));
        CREATE TABLE cp6_capacity_v1.operations(
          environment_id uuid NOT NULL,sandbox_id uuid NOT NULL,tenant_id uuid NOT NULL,domain_key text NOT NULL,operation_key text NOT NULL,
          input_bytes bytea NOT NULL,input_digest text NOT NULL CHECK(input_digest=encode(sha256(input_bytes),'hex')),condition_bytes bytea NOT NULL,
          result_bytes bytea NOT NULL,result_digest text NOT NULL CHECK(result_digest=encode(sha256(result_bytes),'hex')),
          PRIMARY KEY(environment_id,sandbox_id,tenant_id,domain_key,operation_key),
          FOREIGN KEY(environment_id,sandbox_id,tenant_id,domain_key) REFERENCES cp6_capacity_v1.domains);
        CREATE TABLE cp6_capacity_v1.shares(
          environment_id uuid NOT NULL,sandbox_id uuid NOT NULL,tenant_id uuid NOT NULL,domain_key text NOT NULL,share_key text NOT NULL,
          kind text NOT NULL CHECK(kind IN ('PHYSICAL','UNRECEIVED')),range_from numeric(21,8) NOT NULL CHECK(range_from>=0),range_to numeric(21,8) NOT NULL,
          capacity_quantity numeric(21,8) NOT NULL CHECK(capacity_quantity>=0),body_bytes bytea NOT NULL,digest text NOT NULL CHECK(digest=encode(sha256(body_bytes),'hex')),
          operation_key text NOT NULL,CHECK(range_to>range_from),PRIMARY KEY(environment_id,sandbox_id,tenant_id,domain_key,share_key),
          FOREIGN KEY(environment_id,sandbox_id,tenant_id,domain_key,operation_key) REFERENCES cp6_capacity_v1.operations DEFERRABLE INITIALLY DEFERRED);
        CREATE TABLE cp6_capacity_v1.aliases(
          environment_id uuid NOT NULL,sandbox_id uuid NOT NULL,tenant_id uuid NOT NULL,domain_key text NOT NULL,alias_key text NOT NULL,share_key text NOT NULL,
          body_bytes bytea NOT NULL,digest text NOT NULL CHECK(digest=encode(sha256(body_bytes),'hex')),operation_key text NOT NULL,
          PRIMARY KEY(environment_id,sandbox_id,tenant_id,domain_key,alias_key),
          FOREIGN KEY(environment_id,sandbox_id,tenant_id,domain_key,share_key) REFERENCES cp6_capacity_v1.shares,
          FOREIGN KEY(environment_id,sandbox_id,tenant_id,domain_key,operation_key) REFERENCES cp6_capacity_v1.operations DEFERRABLE INITIALLY DEFERRED);
        CREATE TABLE cp6_capacity_v1.used_ranges(
          environment_id uuid NOT NULL,sandbox_id uuid NOT NULL,tenant_id uuid NOT NULL,domain_key text NOT NULL,share_key text NOT NULL,operation_key text NOT NULL,
          kind text NOT NULL CHECK(kind IN ('REALIZED','RELEASED')),range_from numeric(21,8) NOT NULL CHECK(range_from>=0),range_to numeric(21,8) NOT NULL,
          capacity_quantity numeric(21,8) NOT NULL CHECK(capacity_quantity>=0),CHECK(range_to>range_from),
          PRIMARY KEY(environment_id,sandbox_id,tenant_id,domain_key,share_key,operation_key),
          FOREIGN KEY(environment_id,sandbox_id,tenant_id,domain_key,share_key) REFERENCES cp6_capacity_v1.shares,
          FOREIGN KEY(environment_id,sandbox_id,tenant_id,domain_key,operation_key) REFERENCES cp6_capacity_v1.operations DEFERRABLE INITIALLY DEFERRED);
        CREATE FUNCTION cp6_capacity_v1.domain_update() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF (NEW.environment_id,NEW.sandbox_id,NEW.tenant_id,NEW.domain_key,NEW.identity_bytes,NEW.identity_digest)
            IS DISTINCT FROM (OLD.environment_id,OLD.sandbox_id,OLD.tenant_id,OLD.domain_key,OLD.identity_bytes,OLD.identity_digest)
            OR NEW.revision<>OLD.revision+1 THEN RAISE EXCEPTION 'CAPACITY_DOMAIN_UPDATE_INVALID'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER capacity_domain_update BEFORE UPDATE ON cp6_capacity_v1.domains FOR EACH ROW EXECUTE FUNCTION cp6_capacity_v1.domain_update();
        CREATE TRIGGER capacity_domain_permanent BEFORE DELETE OR TRUNCATE ON cp6_capacity_v1.domains FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE FUNCTION cp6_capacity_v1.use_validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE s cp6_capacity_v1.shares%ROWTYPE;
        BEGIN
          PERFORM 1 FROM cp6_capacity_v1.domains WHERE environment_id=NEW.environment_id AND sandbox_id=NEW.sandbox_id AND tenant_id=NEW.tenant_id AND domain_key=NEW.domain_key FOR UPDATE;
          SELECT * INTO STRICT s FROM cp6_capacity_v1.shares WHERE environment_id=NEW.environment_id AND sandbox_id=NEW.sandbox_id AND tenant_id=NEW.tenant_id AND domain_key=NEW.domain_key AND share_key=NEW.share_key;
          IF NEW.range_from<s.range_from OR NEW.range_to>s.range_to OR (NEW.kind='REALIZED' AND s.kind<>'UNRECEIVED')
            OR NEW.capacity_quantity*(s.range_to-s.range_from)<>(NEW.range_to-NEW.range_from)*s.capacity_quantity
            OR EXISTS(SELECT FROM cp6_capacity_v1.used_ranges WHERE environment_id=NEW.environment_id AND sandbox_id=NEW.sandbox_id AND tenant_id=NEW.tenant_id
              AND domain_key=NEW.domain_key AND share_key=NEW.share_key AND range_from<NEW.range_to AND range_to>NEW.range_from)
          THEN RAISE EXCEPTION 'CAPACITY_SHARE_USE_INVALID'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER capacity_use_validate BEFORE INSERT ON cp6_capacity_v1.used_ranges FOR EACH ROW EXECUTE FUNCTION cp6_capacity_v1.use_validate();
        CREATE FUNCTION cp6_capacity_v1.balance_validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE d cp6_capacity_v1.domains%ROWTYPE; p numeric; u numeric;
        BEGIN
          SELECT * INTO STRICT d FROM cp6_capacity_v1.domains WHERE environment_id=NEW.environment_id AND sandbox_id=NEW.sandbox_id AND tenant_id=NEW.tenant_id AND domain_key=NEW.domain_key FOR UPDATE;
          SELECT COALESCE(sum(s.capacity_quantity-COALESCE(x.used,0)) FILTER(WHERE s.kind='PHYSICAL'),0),
                 COALESCE(sum(s.capacity_quantity-COALESCE(x.used,0)) FILTER(WHERE s.kind='UNRECEIVED'),0)
          INTO p,u FROM cp6_capacity_v1.shares s LEFT JOIN LATERAL
            (SELECT sum(r.capacity_quantity) AS used FROM cp6_capacity_v1.used_ranges r WHERE r.environment_id=s.environment_id AND r.sandbox_id=s.sandbox_id AND r.tenant_id=s.tenant_id AND r.domain_key=s.domain_key AND r.share_key=s.share_key) x ON true
          WHERE s.environment_id=d.environment_id AND s.sandbox_id=d.sandbox_id AND s.tenant_id=d.tenant_id AND s.domain_key=d.domain_key;
          IF d.physical<>p OR d.unreceived<>u OR d.revision<>(SELECT count(*) FROM cp6_capacity_v1.operations o WHERE o.environment_id=d.environment_id AND o.sandbox_id=d.sandbox_id AND o.tenant_id=d.tenant_id AND o.domain_key=d.domain_key)
          THEN RAISE EXCEPTION 'CAPACITY_BALANCE_INVALID'; END IF;
          RETURN NULL;
        END $$;
        DO $$ DECLARE tbl text; BEGIN
          FOREACH tbl IN ARRAY ARRAY['domains','operations','shares','aliases','used_ranges'] LOOP
            EXECUTE format('CREATE CONSTRAINT TRIGGER capacity_balance AFTER INSERT OR UPDATE ON cp6_capacity_v1.%I DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_capacity_v1.balance_validate()',tbl);
          END LOOP;
          FOREACH tbl IN ARRAY ARRAY['operations','shares','aliases','used_ranges'] LOOP
            EXECUTE format('CREATE TRIGGER capacity_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_capacity_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()',tbl);
          END LOOP;
        END $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_capacity_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_capacity_v1 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Capacity originals require forward recovery; do not drop them.");
}
