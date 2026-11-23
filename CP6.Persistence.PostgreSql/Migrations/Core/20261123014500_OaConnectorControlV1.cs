using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261123014500_OaConnectorControlV1")]
public sealed class OaConnectorControlV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_oa_conn_v1;

        -- 安全 Owner 在可信环境登记依赖。版本内容不可改，撤销使用独立开关。
        CREATE TABLE cp6_oa_conn_v1.profiles (
            tenant_id uuid NOT NULL, environment_id uuid NOT NULL, id uuid NOT NULL,
            kind text NOT NULL CHECK(kind IN ('TIMER','EVENT','MESSAGE','CONNECTOR')),
            label text NOT NULL, content jsonb NOT NULL, evidence_ref text,
            adoption text NOT NULL CHECK(adoption IN ('UNPROVEN','ADOPTED','SIMULATED')),
            PRIMARY KEY(tenant_id,environment_id,id));
        CREATE TABLE cp6_oa_conn_v1.profile_control (
            tenant_id uuid NOT NULL, environment_id uuid NOT NULL, profile_id uuid NOT NULL,
            enabled boolean NOT NULL, revision uuid NOT NULL DEFAULT gen_random_uuid(),
            PRIMARY KEY(tenant_id,environment_id,profile_id),
            FOREIGN KEY(tenant_id,environment_id,profile_id) REFERENCES cp6_oa_conn_v1.profiles);
        CREATE TABLE cp6_oa_conn_v1.roots (
            tenant_id uuid NOT NULL, environment_id uuid NOT NULL, id uuid NOT NULL,
            kind text NOT NULL CHECK(kind IN ('TRIGGER','CONNECTOR')), source_kind text NOT NULL,
            name text NOT NULL CHECK(length(name) BETWEEN 1 AND 100),
            owner_id uuid NOT NULL, revision uuid NOT NULL, latest_version uuid NOT NULL, active_version uuid,
            PRIMARY KEY(tenant_id,environment_id,id),
            CHECK((kind='CONNECTOR' AND source_kind IN ('TENANT','APP')) OR
                  (kind='TRIGGER' AND source_kind IN ('TIMER','EVENT','MESSAGE'))));
        CREATE UNIQUE INDEX root_natural_key ON cp6_oa_conn_v1.roots(tenant_id,environment_id,kind,source_kind,lower(name));
        CREATE TABLE cp6_oa_conn_v1.versions (
            tenant_id uuid NOT NULL, environment_id uuid NOT NULL, root_id uuid NOT NULL, id uuid NOT NULL,
            number integer NOT NULL CHECK(number>0), maker uuid NOT NULL, profile_id uuid NOT NULL,
            display_name text NOT NULL, content jsonb NOT NULL, created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY(tenant_id,environment_id,root_id,id), UNIQUE(tenant_id,environment_id,root_id,number),
            FOREIGN KEY(tenant_id,environment_id,root_id) REFERENCES cp6_oa_conn_v1.roots,
            FOREIGN KEY(tenant_id,environment_id,profile_id) REFERENCES cp6_oa_conn_v1.profiles);
        ALTER TABLE cp6_oa_conn_v1.roots ADD FOREIGN KEY(tenant_id,environment_id,id,latest_version)
            REFERENCES cp6_oa_conn_v1.versions DEFERRABLE INITIALLY DEFERRED,
            ADD FOREIGN KEY(tenant_id,environment_id,id,active_version) REFERENCES cp6_oa_conn_v1.versions DEFERRABLE INITIALLY DEFERRED;
        CREATE TABLE cp6_oa_conn_v1.version_control (
            tenant_id uuid NOT NULL, environment_id uuid NOT NULL, root_id uuid NOT NULL, version_id uuid NOT NULL,
            published boolean NOT NULL DEFAULT false, state text NOT NULL CHECK(state IN ('DRAFT','DISABLED','ENABLED','RETIRED')),
            PRIMARY KEY(tenant_id,environment_id,root_id,version_id),
            FOREIGN KEY(tenant_id,environment_id,root_id,version_id) REFERENCES cp6_oa_conn_v1.versions);
        CREATE TABLE cp6_oa_conn_v1.previews (
            tenant_id uuid NOT NULL, environment_id uuid NOT NULL, id uuid NOT NULL, root_id uuid NOT NULL, version_id uuid NOT NULL,
            action text NOT NULL CHECK(action IN ('ENABLE','ACTIVATE','DISABLE','RETIRE')),
            heads jsonb NOT NULL, manifest jsonb NOT NULL, expires_at timestamptz NOT NULL,
            coverage text NOT NULL CHECK(coverage IN ('COMPLETE','PARTIAL_UNPROVEN')),
            PRIMARY KEY(tenant_id,environment_id,id),
            FOREIGN KEY(tenant_id,environment_id,root_id,version_id) REFERENCES cp6_oa_conn_v1.versions);
        CREATE TABLE cp6_oa_conn_v1.subjects (
            tenant_id uuid NOT NULL, environment_id uuid NOT NULL, id uuid NOT NULL, root_id uuid NOT NULL, version_id uuid NOT NULL,
            kind text NOT NULL CHECK(kind IN ('TRIGGER_PUBLISH','TRIGGER_ENABLE','TRIGGER_RETIRE','CONNECTOR_ACTIVATE','CONNECTOR_RETIRE')),
            impact_ref uuid, heads jsonb NOT NULL, maker uuid NOT NULL,
            CHECK((kind='TRIGGER_PUBLISH')=(impact_ref IS NULL)),
            PRIMARY KEY(tenant_id,environment_id,id),
            FOREIGN KEY(tenant_id,environment_id,root_id,version_id) REFERENCES cp6_oa_conn_v1.versions,
            FOREIGN KEY(tenant_id,environment_id,impact_ref) REFERENCES cp6_oa_conn_v1.previews);
        CREATE TABLE cp6_oa_conn_v1.decisions (
            tenant_id uuid NOT NULL, environment_id uuid NOT NULL, id uuid NOT NULL, subject_id uuid NOT NULL,
            reviewer uuid NOT NULL, decision text NOT NULL CHECK(decision IN ('APPROVE','REJECT')),
            authority_epoch bigint NOT NULL, decided_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY(tenant_id,environment_id,id), UNIQUE(tenant_id,environment_id,subject_id),
            FOREIGN KEY(tenant_id,environment_id,subject_id) REFERENCES cp6_oa_conn_v1.subjects);
        CREATE TABLE cp6_oa_conn_v1.commands (
            tenant_id uuid NOT NULL, environment_id uuid NOT NULL, actor uuid NOT NULL, command_id text NOT NULL,
            operation text NOT NULL, target uuid, digest text NOT NULL, receipt jsonb NOT NULL,
            recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(), PRIMARY KEY(tenant_id,environment_id,actor,command_id));
        CREATE TABLE cp6_oa_conn_v1.audit (
            tenant_id uuid NOT NULL, environment_id uuid NOT NULL, actor uuid NOT NULL, command_id text NOT NULL,
            operation text NOT NULL, root_id uuid, version_id uuid, authority_epoch bigint NOT NULL,
            recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(), PRIMARY KEY(tenant_id,environment_id,actor,command_id),
            FOREIGN KEY(tenant_id,environment_id,actor,command_id) REFERENCES cp6_oa_conn_v1.commands);
        CREATE TABLE cp6_oa_conn_v1.frontiers (
            tenant_id uuid NOT NULL, environment_id uuid NOT NULL, root_id uuid NOT NULL, revision bigint NOT NULL DEFAULT 0,
            PRIMARY KEY(tenant_id,environment_id,root_id), FOREIGN KEY(tenant_id,environment_id,root_id) REFERENCES cp6_oa_conn_v1.roots);
        CREATE TABLE cp6_oa_conn_v1.runtime_members (
            tenant_id uuid NOT NULL, environment_id uuid NOT NULL, root_id uuid NOT NULL, version_id uuid NOT NULL,
            kind text NOT NULL, id uuid NOT NULL, state text NOT NULL,
            CHECK((kind='INVOCATION' AND state IN ('RECEIVED','READY','ADMISSION_BLOCKED','INSTANCE_CREATED','REJECTED','OUTCOME_UNKNOWN'))
                OR (kind='SLOT' AND state IN ('RESERVED','BLOCKED','INSTANCE_CREATED','CANCELLED'))
                OR (kind='INSTANCE' AND state IN ('CREATED','RUNNING','TERMINAL'))
                OR (kind='JOB' AND state IN ('PENDING','CLAIMED','BLOCKED_CONNECTOR','TERMINAL','UNVERIFIED_LEGACY'))
                OR (kind='DISPATCH' AND state IN ('PREPARED','DISPATCH_ADMITTED','SENT','OUTCOME_UNKNOWN','RESULT_RECEIVED','FAILED_NO_EFFECT','QUARANTINED'))),
            PRIMARY KEY(tenant_id,environment_id,root_id,kind,id),
            FOREIGN KEY(tenant_id,environment_id,root_id,version_id) REFERENCES cp6_oa_conn_v1.versions);

        -- 预览与各写者共用 Main 身份事务锁。成员插入又删除仍会推进单调前沿。
        CREATE FUNCTION cp6_oa_conn_v1.name_lock(tenant uuid, name text) RETURNS bigint
        LANGUAGE sql IMMUTABLE SET search_path=pg_catalog AS $$
            SELECT hashtextextended('oa-connector/'||tenant::text||'/'||lower(name),0)
        $$;
        CREATE FUNCTION cp6_oa_conn_v1.action_ref(raw text) RETURNS jsonb LANGUAGE plpgsql IMMUTABLE SET search_path=pg_catalog AS $$
        BEGIN RETURN raw::jsonb;
        EXCEPTION WHEN invalid_text_representation THEN RETURN NULL;
        END $$;
        CREATE FUNCTION cp6_oa_conn_v1.job_matches(root_id uuid, root_name text, action_ref jsonb)
        RETURNS boolean LANGUAGE sql IMMUTABLE SET search_path=pg_catalog AS $$
            SELECT CASE WHEN jsonb_typeof(action_ref->'connectorPin')='object' THEN action_ref #>> '{connectorPin,rootId}'=root_id::text
                        ELSE lower(action_ref->>'connectorName')=lower(root_name) END
        $$;
        CREATE FUNCTION cp6_oa_conn_v1.unknown_job(kind text, action_ref jsonb) RETURNS boolean
        LANGUAGE sql IMMUTABLE SET search_path=pg_catalog AS $$
            SELECT kind IN ('webApi','deferredWebApiStart') AND coalesce(action_ref #>> '{connectorPin,rootId}',action_ref->>'connectorName','')=''
        $$;
        CREATE FUNCTION cp6_oa_conn_v1.advance(row_data jsonb, source text) RETURNS void
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF row_data IS NULL THEN RETURN; END IF;
            IF source='runtime_members' THEN
                UPDATE cp6_oa_conn_v1.frontiers SET revision=revision+1
                WHERE tenant_id=(row_data->>'tenant_id')::uuid AND environment_id=(row_data->>'environment_id')::uuid
                  AND root_id=(row_data->>'root_id')::uuid;
            ELSIF source='Wf_TriggerFire' THEN
                UPDATE cp6_oa_conn_v1.frontiers SET revision=revision+1
                WHERE tenant_id=(row_data->>'TenantId')::uuid AND root_id=(row_data->>'TriggerId')::uuid;
            ELSE
                UPDATE cp6_oa_conn_v1.frontiers f SET revision=f.revision+1 FROM cp6_oa_conn_v1.roots r
                WHERE (r.tenant_id,r.environment_id,r.id)=(f.tenant_id,f.environment_id,f.root_id)
                  AND r.tenant_id=(row_data->>'TenantId')::uuid AND
                  ((r.kind='CONNECTOR' AND (cp6_oa_conn_v1.job_matches(r.id,r.name,cp6_oa_conn_v1.action_ref(row_data->>'ActionRefJson'))
                    OR cp6_oa_conn_v1.unknown_job(row_data->>'Kind',cp6_oa_conn_v1.action_ref(row_data->>'ActionRefJson'))))
                    OR (r.kind='TRIGGER' AND EXISTS (SELECT FROM public."Wf_TriggerFire" fire
                        WHERE fire."TenantId"=r.tenant_id AND fire."TriggerId"=r.id
                          AND fire."InstanceId"=(row_data->>'InstanceId')::uuid)));
            END IF;
        END $$;
        CREATE FUNCTION cp6_oa_conn_v1.advance_frontier() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF TG_OP<>'INSERT' THEN PERFORM cp6_oa_conn_v1.advance(to_jsonb(OLD),TG_TABLE_NAME); END IF;
            IF TG_OP<>'DELETE' THEN PERFORM cp6_oa_conn_v1.advance(to_jsonb(NEW),TG_TABLE_NAME); END IF;
            RETURN NULL;
        END $$;
        -- 历史配置交接后，旧配置写者及两段式预约不再能启动实例。
        CREATE FUNCTION cp6_oa_conn_v1.guard_legacy() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE image jsonb; prior jsonb; t uuid; root_key uuid;
        BEGIN
            image=CASE WHEN TG_OP='DELETE' THEN to_jsonb(OLD) ELSE to_jsonb(NEW) END;
            IF TG_OP='UPDATE' THEN prior=to_jsonb(OLD); END IF;
            t=(image->>'TenantId')::uuid;
            root_key=coalesce(image->>'TriggerId',image->>'Id')::uuid;
            IF EXISTS (SELECT FROM cp6_oa_conn_v1.roots r WHERE r.tenant_id=t AND
                r.environment_id=(SELECT environment_id FROM cp6_scope_authorization_v1.environment WHERE singleton) AND
                ((r.id=root_key AND r.kind=CASE WHEN TG_TABLE_NAME='Wf_Connector' THEN 'CONNECTOR' ELSE 'TRIGGER' END)
                 OR (TG_TABLE_NAME='Wf_Connector' AND r.kind='CONNECTOR' AND r.source_kind='TENANT'
                     AND lower(r.name) IN (lower(image->>'Name'),lower(prior->>'Name'))))) THEN
                RAISE EXCEPTION USING ERRCODE='23514',MESSAGE='TRUSTED_CONN_V1_REQUIRED';
            END IF;
            IF TG_OP='DELETE' THEN RETURN OLD; END IF;
            RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_oa_conn_v1.guard_admission() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF TG_OP='DELETE' THEN
                IF OLD.kind='DISPATCH' AND OLD.state<>'PREPARED' THEN
                    RAISE EXCEPTION USING ERRCODE='23514',MESSAGE='DISPATCH_FACT_REQUIRED';
                END IF;
                RETURN OLD;
            END IF;
            IF TG_OP='UPDATE' AND OLD.kind='DISPATCH' AND OLD.state<>'PREPARED'
               AND (NEW.state='PREPARED' OR (OLD.state<>'DISPATCH_ADMITTED' AND NEW.state='DISPATCH_ADMITTED')
                    OR (OLD.state IN ('RESULT_RECEIVED','FAILED_NO_EFFECT','QUARANTINED') AND NEW.state<>OLD.state)) THEN
                RAISE EXCEPTION USING ERRCODE='23514',MESSAGE='DISPATCH_FACT_REQUIRED';
            END IF;
            IF TG_OP='UPDATE' AND (NEW.tenant_id,NEW.environment_id,NEW.root_id,NEW.version_id,NEW.id,NEW.kind)
               IS DISTINCT FROM (OLD.tenant_id,OLD.environment_id,OLD.root_id,OLD.version_id,OLD.id,OLD.kind) THEN
                RAISE EXCEPTION USING ERRCODE='23514',MESSAGE='PIN_MISMATCH';
            END IF;
            -- 已准入的外部请求在停用后仍须能记录原结果。
            IF TG_OP='UPDATE' AND NEW.kind='DISPATCH' AND OLD.state IN ('DISPATCH_ADMITTED','SENT','OUTCOME_UNKNOWN') THEN RETURN NEW; END IF;
            IF ((TG_OP='INSERT' AND NEW.kind IN ('INVOCATION','SLOT','INSTANCE','DISPATCH'))
                OR (NEW.kind IN ('INVOCATION','SLOT') AND NEW.state='INSTANCE_CREATED')
                OR (NEW.kind='DISPATCH' AND NEW.state IN ('DISPATCH_ADMITTED','SENT')))
               AND NOT EXISTS(SELECT FROM cp6_oa_conn_v1.roots WHERE tenant_id=NEW.tenant_id
                   AND environment_id=NEW.environment_id AND id=NEW.root_id AND active_version=NEW.version_id) THEN
                RAISE EXCEPTION USING ERRCODE='23514',MESSAGE='CONTROL_DISABLED';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER oa_conn_admission BEFORE INSERT OR UPDATE OR DELETE ON cp6_oa_conn_v1.runtime_members
            FOR EACH ROW EXECUTE FUNCTION cp6_oa_conn_v1.guard_admission();
        CREATE FUNCTION cp6_oa_conn_v1.change_generation() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN NEW.revision=gen_random_uuid(); RETURN NEW; END $$;
        CREATE TRIGGER new_generation BEFORE UPDATE ON cp6_oa_conn_v1.profile_control FOR EACH ROW
            EXECUTE FUNCTION cp6_oa_conn_v1.change_generation();
        DO $$
        DECLARE rel text;
        BEGIN
            FOREACH rel IN ARRAY ARRAY['profiles','versions','previews','subjects','decisions','commands','audit'] LOOP
                EXECUTE format('CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_oa_conn_v1.%I
                    FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()',rel);
            END LOOP;
            FOREACH rel IN ARRAY ARRAY['public."Wf_ServiceJob"','public."Wf_TriggerFire"','cp6_oa_conn_v1.runtime_members'] LOOP
                EXECUTE format('CREATE TRIGGER oa_conn_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON %s
                    FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change()',rel);
                EXECUTE format('CREATE TRIGGER oa_conn_member AFTER INSERT OR UPDATE OR DELETE ON %s
                    FOR EACH ROW EXECUTE FUNCTION cp6_oa_conn_v1.advance_frontier()',rel);
                EXECUTE format('CREATE TRIGGER oa_conn_no_truncate BEFORE TRUNCATE ON %s
                    FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()',rel);
            END LOOP;
            FOREACH rel IN ARRAY ARRAY['public."Wf_FlowTrigger"','public."Wf_Connector"'] LOOP
                EXECUTE format('CREATE TRIGGER oa_conn_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON %s
                    FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change()',rel);
                EXECUTE format('CREATE TRIGGER oa_conn_no_truncate BEFORE TRUNCATE ON %s
                    FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()',rel);
            END LOOP;
            FOREACH rel IN ARRAY ARRAY['public."Wf_TriggerFire"','public."Wf_FlowTrigger"','public."Wf_Connector"'] LOOP
                EXECUTE format('CREATE TRIGGER oa_conn_legacy BEFORE INSERT OR UPDATE OR DELETE ON %s
                    FOR EACH ROW EXECUTE FUNCTION cp6_oa_conn_v1.guard_legacy()',rel);
            END LOOP;
            FOREACH rel IN ARRAY ARRAY['profiles','profile_control','roots','version_control'] LOOP
                EXECUTE format('CREATE TRIGGER oa_conn_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_oa_conn_v1.%I
                    FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change()',rel);
            END LOOP;
        END $$;
        CREATE FUNCTION cp6_oa_conn_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
            SELECT NOT EXISTS (SELECT FROM unnest(ARRAY['public."Wf_ServiceJob"','public."Wf_TriggerFire"','cp6_oa_conn_v1.runtime_members']) r(rel)
              CROSS JOIN (VALUES ('oa_conn_root','cp6_authentication_v1.before_change()',62),
                                 ('oa_conn_member','cp6_oa_conn_v1.advance_frontier()',29),
                                 ('oa_conn_no_truncate','cp6_scope_authorization_v1.immutable()',34)) x(name,fn,typ)
              WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass(rel) AND t.tgname=name
                 AND t.tgfoid=to_regprocedure(fn) AND t.tgenabled IN ('O','A') AND t.tgtype=typ AND t.tgqual IS NULL AND NOT t.tgisinternal))
            AND NOT EXISTS(SELECT FROM (VALUES
                ('public."Wf_FlowTrigger"','oa_conn_legacy','cp6_oa_conn_v1.guard_legacy()',31),
                ('public."Wf_Connector"','oa_conn_legacy','cp6_oa_conn_v1.guard_legacy()',31),
                ('public."Wf_TriggerFire"','oa_conn_legacy','cp6_oa_conn_v1.guard_legacy()',31),
                ('public."Wf_FlowTrigger"','oa_conn_root','cp6_authentication_v1.before_change()',62),
                ('public."Wf_Connector"','oa_conn_root','cp6_authentication_v1.before_change()',62),
                ('cp6_oa_conn_v1.runtime_members','oa_conn_admission','cp6_oa_conn_v1.guard_admission()',31),
                ('cp6_oa_conn_v1.profile_control','oa_conn_root','cp6_authentication_v1.before_change()',62)
            ) x(rel,name,fn,typ) WHERE NOT EXISTS (SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass(rel)
                AND t.tgname=name AND t.tgfoid=to_regprocedure(fn) AND t.tgenabled IN ('O','A') AND t.tgtype=typ
                AND t.tgqual IS NULL AND NOT t.tgisinternal))
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_oa_conn_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_oa_conn_v1 FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("触发器和连接器的版本、原命令与审批历史通过前向迁移保留。");
}
