using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261026114000_TransferLocalDemandBudgetV1")]
public sealed class TransferLocalDemandBudgetV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_transfer_local_v1;
        REVOKE ALL ON SCHEMA cp6_transfer_local_v1 FROM PUBLIC;
        CREATE TABLE cp6_transfer_local_v1.evidence_ids (
          environment_id uuid NOT NULL,sandbox_id uuid NOT NULL,tenant_id uuid NOT NULL,id uuid NOT NULL,
          kind text NOT NULL CHECK(kind IN ('DEMAND','BINDING')),
          PRIMARY KEY(environment_id,sandbox_id,tenant_id,id));
        CREATE FUNCTION cp6_transfer_local_v1.valid_ranges(r numrange[]) RETURNS boolean
        LANGUAGE sql IMMUTABLE SET search_path=pg_catalog AS $$
          SELECT cardinality(r)>0 AND array_ndims(r)=1 AND array_lower(r,1)=1
            AND NOT EXISTS(SELECT FROM unnest(r) x WHERE x IS NULL OR isempty(x)
              OR lower_inf(x) OR upper_inf(x) OR NOT lower_inc(x) OR upper_inc(x)
              OR lower(x)<0 OR upper(x)>9999999999999.99999999
              OR scale(lower(x))>8 OR scale(upper(x))>8)
            AND NOT EXISTS(SELECT FROM unnest(r) WITH ORDINALITY a(v,i),unnest(r) WITH ORDINALITY b(v,i)
              WHERE a.i<b.i AND a.v && b.v)
        $$;
        CREATE FUNCTION cp6_transfer_local_v1.measure(r numrange[]) RETURNS numeric
        LANGUAGE sql IMMUTABLE SET search_path=pg_catalog AS $$
          SELECT sum(upper(x)-lower(x)) FROM unnest(r) x
        $$;
        CREATE FUNCTION cp6_transfer_local_v1.valid_map(a numrange[],b numrange[],q numeric) RETURNS boolean
        LANGUAGE sql IMMUTABLE SET search_path=pg_catalog AS $$
          SELECT cp6_transfer_local_v1.valid_ranges(a) AND cp6_transfer_local_v1.valid_ranges(b)
            AND cardinality(a)=cardinality(b) AND lower(a[1])=0 AND upper(a[cardinality(a)])=q
            AND NOT EXISTS(SELECT FROM generate_subscripts(a,1) i
              WHERE upper(a[i])-lower(a[i])<>upper(b[i])-lower(b[i])
                OR (i>1 AND upper(a[i-1])<>lower(a[i])))
        $$;
        CREATE TABLE cp6_transfer_local_v1.budgets (
          environment_id uuid NOT NULL CHECK(environment_id<>'00000000-0000-0000-0000-000000000000'),
          sandbox_id uuid NOT NULL, tenant_id uuid NOT NULL CHECK(tenant_id<>'00000000-0000-0000-0000-000000000000'),
          mode text COLLATE "C" NOT NULL CHECK(mode IN ('PRODUCTION','SIMULATION')),
          budget_key text COLLATE "C" NOT NULL CHECK(length(budget_key)>0),
          demand_id uuid NOT NULL CHECK(demand_id<>'00000000-0000-0000-0000-000000000000'),
          current_version bigint NOT NULL CHECK(current_version>0), revision bigint NOT NULL CHECK(revision>0),
          identity_bytes bytea NOT NULL,
          PRIMARY KEY(environment_id,sandbox_id,tenant_id,budget_key),
          UNIQUE(environment_id,sandbox_id,tenant_id,demand_id),
          CHECK((mode='PRODUCTION')=(sandbox_id='00000000-0000-0000-0000-000000000000')));
        CREATE TABLE cp6_transfer_local_v1.demand_versions (
          environment_id uuid NOT NULL,sandbox_id uuid NOT NULL,tenant_id uuid NOT NULL,budget_key text COLLATE "C" NOT NULL,
          version bigint NOT NULL CHECK(version>0), body_bytes bytea NOT NULL,digest text COLLATE "C" NOT NULL,
          state text NOT NULL CHECK(state IN ('OPEN','STOPPED','CLOSED')),
          quantity numeric(21,8) NOT NULL CHECK(quantity>0),ranges numrange[] NOT NULL,
          PRIMARY KEY(environment_id,sandbox_id,tenant_id,budget_key,version),
          FOREIGN KEY(environment_id,sandbox_id,tenant_id,budget_key)
            REFERENCES cp6_transfer_local_v1.budgets(environment_id,sandbox_id,tenant_id,budget_key),
          CHECK(cp6_transfer_local_v1.valid_ranges(ranges)),
          CHECK(cp6_transfer_local_v1.measure(ranges)=quantity),
          CHECK(digest=encode(sha256(body_bytes),'hex')));
        ALTER TABLE cp6_transfer_local_v1.budgets ADD CONSTRAINT current_version_exists
          FOREIGN KEY(environment_id,sandbox_id,tenant_id,budget_key,current_version)
          REFERENCES cp6_transfer_local_v1.demand_versions(environment_id,sandbox_id,tenant_id,budget_key,version)
          DEFERRABLE INITIALLY DEFERRED;
        CREATE TABLE cp6_transfer_local_v1.bindings (
          environment_id uuid NOT NULL,sandbox_id uuid NOT NULL,tenant_id uuid NOT NULL,budget_key text COLLATE "C" NOT NULL,
          id uuid NOT NULL CHECK(id<>'00000000-0000-0000-0000-000000000000'),
          request_id uuid NOT NULL CHECK(request_id<>'00000000-0000-0000-0000-000000000000'),
          line_id uuid NOT NULL CHECK(line_id<>'00000000-0000-0000-0000-000000000000'),
          demand_version bigint NOT NULL,input_bytes bytea NOT NULL,result_bytes bytea NOT NULL,
          state text NOT NULL CHECK(state IN ('ACTIVE','CLOSED')),
          quantity numeric(21,8) NOT NULL CHECK(quantity>0),request_ranges numrange[] NOT NULL,demand_ranges numrange[] NOT NULL,
          created_revision bigint NOT NULL CHECK(created_revision>0),closed_revision bigint,
          PRIMARY KEY(environment_id,sandbox_id,tenant_id,id),
          FOREIGN KEY(environment_id,sandbox_id,tenant_id,budget_key,demand_version)
            REFERENCES cp6_transfer_local_v1.demand_versions(environment_id,sandbox_id,tenant_id,budget_key,version),
          CHECK(cp6_transfer_local_v1.valid_map(request_ranges,demand_ranges,quantity)),
          CHECK((state='ACTIVE' AND closed_revision IS NULL)
            OR (state='CLOSED' AND closed_revision IS NOT NULL AND closed_revision>created_revision)));
        CREATE UNIQUE INDEX one_active_request_line ON cp6_transfer_local_v1.bindings
          (environment_id,sandbox_id,tenant_id,request_id,line_id) WHERE state='ACTIVE';
        CREATE INDEX budget_bindings ON cp6_transfer_local_v1.bindings(environment_id,sandbox_id,tenant_id,budget_key);
        CREATE FUNCTION cp6_transfer_local_v1.immutable() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN RAISE EXCEPTION 'TRANSFER_ORIGINAL_IMMUTABLE' USING ERRCODE='23514'; END $$;
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_transfer_local_v1.demand_versions
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_transfer_local_v1.immutable();
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_transfer_local_v1.evidence_ids
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_transfer_local_v1.immutable();
        CREATE FUNCTION cp6_transfer_local_v1.claim_evidence_id() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          INSERT INTO cp6_transfer_local_v1.evidence_ids VALUES
            (NEW.environment_id,NEW.sandbox_id,NEW.tenant_id,
             CASE WHEN TG_TABLE_NAME='budgets' THEN (to_jsonb(NEW)->>'demand_id')::uuid ELSE (to_jsonb(NEW)->>'id')::uuid END,
             CASE WHEN TG_TABLE_NAME='budgets' THEN 'DEMAND' ELSE 'BINDING' END);
          RETURN NEW;
        END $$;
        CREATE TRIGGER evidence_id BEFORE INSERT ON cp6_transfer_local_v1.budgets
          FOR EACH ROW EXECUTE FUNCTION cp6_transfer_local_v1.claim_evidence_id();
        CREATE TRIGGER evidence_id BEFORE INSERT ON cp6_transfer_local_v1.bindings
          FOR EACH ROW EXECUTE FUNCTION cp6_transfer_local_v1.claim_evidence_id();
        CREATE TRIGGER no_delete BEFORE DELETE OR TRUNCATE ON cp6_transfer_local_v1.bindings
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_transfer_local_v1.immutable();
        CREATE TRIGGER no_delete BEFORE DELETE OR TRUNCATE ON cp6_transfer_local_v1.budgets
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_transfer_local_v1.immutable();
        CREATE FUNCTION cp6_transfer_local_v1.binding_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE b cp6_transfer_local_v1.budgets; d cp6_transfer_local_v1.demand_versions;
        BEGIN
          SELECT * INTO STRICT b FROM cp6_transfer_local_v1.budgets WHERE
            (environment_id,sandbox_id,tenant_id,budget_key)=(NEW.environment_id,NEW.sandbox_id,NEW.tenant_id,NEW.budget_key) FOR UPDATE;
          IF TG_OP='UPDATE' THEN
            IF OLD.state<>'ACTIVE' OR NEW.state<>'CLOSED'
              OR (to_jsonb(OLD)-'state'-'closed_revision') IS DISTINCT FROM (to_jsonb(NEW)-'state'-'closed_revision')
              OR NEW.closed_revision IS DISTINCT FROM b.revision THEN
              RAISE EXCEPTION 'TRANSFER_BINDING_TERMINAL' USING ERRCODE='23514';
            END IF;
            RETURN NEW;
          END IF;
          SELECT * INTO STRICT d FROM cp6_transfer_local_v1.demand_versions WHERE
            (environment_id,sandbox_id,tenant_id,budget_key,version)=
            (NEW.environment_id,NEW.sandbox_id,NEW.tenant_id,NEW.budget_key,b.current_version);
          IF NEW.state<>'ACTIVE' OR d.state<>'OPEN' OR NEW.demand_version<>b.current_version
            OR NEW.created_revision<>b.revision OR NOT COALESCE((SELECT bool_and(x <@ r.coverage)
              FROM unnest(NEW.demand_ranges) x CROSS JOIN (SELECT range_agg(y) coverage FROM unnest(d.ranges) y) r),false)
            OR EXISTS(SELECT FROM cp6_transfer_local_v1.bindings existing_binding,unnest(existing_binding.demand_ranges) a,unnest(NEW.demand_ranges) z
              WHERE (existing_binding.environment_id,existing_binding.sandbox_id,existing_binding.tenant_id,existing_binding.budget_key)=
                (NEW.environment_id,NEW.sandbox_id,NEW.tenant_id,NEW.budget_key) AND existing_binding.state='ACTIVE' AND a && z) THEN
            RAISE EXCEPTION 'SOURCE_BUDGET_CONFLICT' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER binding_guard BEFORE INSERT OR UPDATE ON cp6_transfer_local_v1.bindings
          FOR EACH ROW EXECUTE FUNCTION cp6_transfer_local_v1.binding_guard();
        CREATE FUNCTION cp6_transfer_local_v1.budget_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE d cp6_transfer_local_v1.demand_versions; old_state text;
        BEGIN
          IF (to_jsonb(OLD)-'current_version'-'revision') IS DISTINCT FROM (to_jsonb(NEW)-'current_version'-'revision')
            OR NEW.revision<>OLD.revision+1 OR NEW.current_version NOT IN (OLD.current_version,OLD.current_version+1) THEN
            RAISE EXCEPTION 'TRANSFER_BUDGET_IDENTITY_IMMUTABLE' USING ERRCODE='23514';
          END IF;
          IF NEW.current_version<>OLD.current_version THEN
            SELECT * INTO STRICT d FROM cp6_transfer_local_v1.demand_versions WHERE
              (environment_id,sandbox_id,tenant_id,budget_key,version)=
              (NEW.environment_id,NEW.sandbox_id,NEW.tenant_id,NEW.budget_key,NEW.current_version);
            SELECT state INTO STRICT old_state FROM cp6_transfer_local_v1.demand_versions WHERE
              (environment_id,sandbox_id,tenant_id,budget_key,version)=
              (OLD.environment_id,OLD.sandbox_id,OLD.tenant_id,OLD.budget_key,OLD.current_version);
            IF old_state='CLOSED' OR EXISTS(SELECT FROM cp6_transfer_local_v1.bindings b,unnest(b.demand_ranges) x
              WHERE (b.environment_id,b.sandbox_id,b.tenant_id,b.budget_key)=
                (NEW.environment_id,NEW.sandbox_id,NEW.tenant_id,NEW.budget_key) AND b.state='ACTIVE'
                AND NOT x <@ (SELECT range_agg(y) FROM unnest(d.ranges) y)) THEN
              RAISE EXCEPTION 'SOURCE_BUDGET_CONFLICT' USING ERRCODE='23514';
            END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER budget_guard BEFORE UPDATE ON cp6_transfer_local_v1.budgets
          FOR EACH ROW EXECUTE FUNCTION cp6_transfer_local_v1.budget_guard();
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_transfer_local_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_transfer_local_v1 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Source demand and original bindings require forward recovery.");
}
