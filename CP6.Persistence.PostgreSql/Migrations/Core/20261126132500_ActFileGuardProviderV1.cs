using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261126132500_ActFileGuardProviderV1")]
public sealed class ActFileGuardProviderV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_act_file_v1;
        CREATE SCHEMA cp6_act_file_transport;
        REVOKE ALL ON SCHEMA cp6_act_file_v1,cp6_act_file_transport FROM PUBLIC;

        CREATE TABLE cp6_act_file_v1.sources (
            tenant_id uuid PRIMARY KEY REFERENCES public."Sys_Tenants"("Id"),
            store_id uuid NOT NULL UNIQUE,
            server_name name NOT NULL UNIQUE,
            grant_login name NOT NULL DEFAULT session_user,
            epoch bigint NOT NULL CHECK(epoch>0),
            receipt uuid NOT NULL,
            advanced_in xid8
        );
        CREATE TRIGGER act_file_source_permanent BEFORE DELETE OR TRUNCATE ON cp6_act_file_v1.sources
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        ALTER TABLE cp6_act_file_v1.sources ENABLE ALWAYS TRIGGER act_file_source_permanent;
        CREATE TABLE cp6_act_file_v1.grants (
            tenant_id uuid NOT NULL REFERENCES cp6_act_file_v1.sources(tenant_id),
            actor_id uuid NOT NULL,
            file_id uuid NOT NULL,
            purpose text NOT NULL CHECK(purpose='ACTIVITY_EVIDENCE'),
            can_add boolean NOT NULL,
            can_retain boolean NOT NULL,
            can_unlink boolean NOT NULL,
            can_download boolean NOT NULL,
            retention_ref uuid,
            policy_version uuid NOT NULL DEFAULT gen_random_uuid(),
            CHECK(NOT can_retain OR retention_ref IS NOT NULL),
            CHECK(retention_ref IS NULL OR retention_ref<>'00000000-0000-0000-0000-000000000000'),
            PRIMARY KEY(tenant_id,actor_id,file_id,purpose),
            FOREIGN KEY(tenant_id,file_id) REFERENCES cp6_file_content_v1.contents(tenant_id,file_id)
        );
        CREATE INDEX grants_by_file_v1 ON cp6_act_file_v1.grants(tenant_id,file_id);
        CREATE TABLE cp6_act_file_v1.proof_receipts (
            tenant_id uuid NOT NULL REFERENCES cp6_act_file_v1.sources(tenant_id),
            actor_id uuid NOT NULL,
            client_id text COLLATE "C" NOT NULL,
            operation_kind text COLLATE "C" NOT NULL,
            operation_id uuid NOT NULL,
            input_digest text NOT NULL,
            proof bytea NOT NULL,
            occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY(tenant_id,actor_id,operation_kind,operation_id)
        );
        CREATE TABLE cp6_act_file_v1.stream_audit (
            tenant_id uuid NOT NULL REFERENCES cp6_act_file_v1.sources(tenant_id),
            delivery_id uuid NOT NULL,
            actor_id uuid NOT NULL,
            file_id uuid NOT NULL,
            operation_id uuid NOT NULL,
            proof_digest text NOT NULL,
            state text NOT NULL CHECK(state IN ('STARTED','COMPLETED','REVOKED','UNPROVEN','INTERRUPTED')),
            confirmed_bytes bigint NOT NULL CHECK(confirmed_bytes>=0),
            occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY(tenant_id,delivery_id,state)
        );
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_act_file_v1.proof_receipts
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_act_file_v1.stream_audit
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();

        CREATE FUNCTION cp6_act_file_v1.hold_source_v1(t uuid)
        RETURNS TABLE(store_id uuid,epoch bigint,receipt uuid)
        LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $$
            SELECT s.store_id,s.epoch,s.receipt FROM cp6_act_file_v1.sources s WHERE s.tenant_id=t FOR SHARE
        $$;
        REVOKE ALL ON FUNCTION cp6_act_file_v1.hold_source_v1(uuid) FROM PUBLIC;

        -- 仅受控迁移角色登记来源。运行角色不能更改绑定、代次或使用任意跨库 SQL。
        CREATE FUNCTION cp6_act_file_v1.advance_sources_v1(affected uuid) RETURNS void
        LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $$
        DECLARE source cp6_act_file_v1.sources;
        DECLARE next_epoch bigint;
        DECLARE next_receipt uuid;
        DECLARE xid xid8 := pg_current_xact_id();
        BEGIN
            FOR source IN SELECT * FROM cp6_act_file_v1.sources s
                WHERE affected IS NULL OR s.tenant_id=affected ORDER BY s.tenant_id FOR UPDATE
            LOOP
                IF source.advanced_in=xid THEN CONTINUE; END IF;
                -- 服务器名称来自受控目录，且必须是实际登记的 dblink 外部服务器。
                IF NOT EXISTS (
                    SELECT 1 FROM pg_foreign_server s JOIN pg_foreign_data_wrapper w ON w.oid=s.srvfdw
                    WHERE s.srvname=source.server_name AND w.fdwname='dblink_fdw'
                      AND EXISTS(SELECT 1 FROM pg_options_to_table(s.srvoptions) WHERE option_name='connect_timeout' AND option_value='5')
                      AND (
                        EXISTS(SELECT 1 FROM pg_options_to_table(s.srvoptions) WHERE option_name='sslmode' AND option_value='verify-full')
                        OR EXISTS(SELECT 1 FROM pg_options_to_table(s.srvoptions) WHERE option_name='host' AND option_value IN ('127.0.0.1','::1'))
                      )
                ) THEN
                    RAISE EXCEPTION 'ACT_FILE_GUARD_UNPROVEN' USING ERRCODE='23514';
                END IF;
                BEGIN
                    -- 远端只执行已登记函数。远端提交确认先于 Main 当前事务的业务提交。
                    SELECT d.epoch,d.receipt INTO STRICT next_epoch,next_receipt
                    FROM cp6_act_file_transport.dblink(source.server_name::text,format(
                        'SELECT epoch,receipt FROM crm_act_file_guard_v1.advance_v1(%L::uuid,%L::uuid,%L::bigint,%L::uuid)',
                        source.tenant_id,source.store_id,source.epoch,source.receipt))
                        AS d(epoch bigint,receipt uuid);
                EXCEPTION WHEN OTHERS THEN
                    RAISE EXCEPTION 'ACT_FILE_GUARD_UNPROVEN' USING ERRCODE='23514';
                END;
                IF next_epoch IS DISTINCT FROM source.epoch+1 OR next_receipt IS NULL OR next_receipt=source.receipt THEN
                    RAISE EXCEPTION 'ACT_FILE_GUARD_UNPROVEN' USING ERRCODE='23514';
                END IF;
                UPDATE cp6_act_file_v1.sources s SET epoch=next_epoch,receipt=next_receipt,advanced_in=xid
                  WHERE s.tenant_id=source.tenant_id;
            END LOOP;
        END $$;
        REVOKE ALL ON FUNCTION cp6_act_file_v1.advance_sources_v1(uuid) FROM PUBLIC;

        CREATE FUNCTION cp6_act_file_v1.source_changed_v1() RETURNS trigger
        LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $$
        DECLARE old_tenant uuid;
        DECLARE new_tenant uuid;
        BEGIN
            IF TG_LEVEL='STATEMENT' THEN
                PERFORM cp6_act_file_v1.advance_sources_v1(NULL);
                RETURN NULL;
            END IF;
            IF TG_OP<>'INSERT' THEN old_tenant := (to_jsonb(OLD)->>TG_ARGV[0])::uuid; END IF;
            IF TG_OP<>'DELETE' THEN new_tenant := (to_jsonb(NEW)->>TG_ARGV[0])::uuid; END IF;
            IF TG_OP='UPDATE' AND to_jsonb(OLD)=to_jsonb(NEW) THEN RETURN NULL; END IF;
            IF old_tenant IS NOT NULL AND new_tenant IS NOT NULL AND old_tenant<>new_tenant THEN
                PERFORM cp6_act_file_v1.advance_sources_v1(least(old_tenant,new_tenant));
                PERFORM cp6_act_file_v1.advance_sources_v1(greatest(old_tenant,new_tenant));
            ELSE
                PERFORM cp6_act_file_v1.advance_sources_v1(coalesce(old_tenant,new_tenant));
            END IF;
            RETURN NULL;
        END $$;
        REVOKE ALL ON FUNCTION cp6_act_file_v1.source_changed_v1() FROM PUBLIC;

        -- 原实体的普通写、批量 SQL 和后台写入均在数据库边界经过同一保护。
        DO $$
        DECLARE item record;
        BEGIN
            FOR item IN SELECT * FROM (VALUES
              ('cp6_file_content_v1.contents','tenant_id'),
              ('cp6_file_content_v1.heads','tenant_id'),
              ('cp6_file_content_v1.events','tenant_id'),
              ('cp6_file_content_v1.draft_bindings','tenant_id'),
              ('cp6_file_content_v1.dispositions','tenant_id'),
              ('cp6_file_content_v1.disposition_attempts','tenant_id'),
              ('cp6_file_content_v1.disposition_completions','tenant_id'),
              ('cp6_audit_v1.policies','tenant_id'),
              ('cp6_audit_v1.safeguards','tenant_id'),
              ('cp6_privacy_disposition_v1.holds','target_tenant'),
              ('cp6_privacy_disposition_v1.policies','target_tenant'),
              ('public."Sys_Tenants"','Id'),
              ('public."Sys_Users"','TenantId'),
              ('public."Sys_Roles"','TenantId'),
              ('public."Sys_UserRole"','TenantId'),
              ('public."Sys_Dept"','TenantId'),
              ('public."Sys_RoleMenus"','TenantId'),
              ('public."Sys_RoleAction"','TenantId'),
              ('public."Sys_RoleDataScope"','TenantId'),
              ('public."Sys_RoleFieldPerm"','TenantId'),
              ('public."Sys_MenuAction"','TenantId')
            ) t(table_name, tenant_column) LOOP
                EXECUTE format('CREATE TRIGGER act_file_source_change AFTER INSERT OR UPDATE OR DELETE ON %s
                    FOR EACH ROW EXECUTE FUNCTION cp6_act_file_v1.source_changed_v1(%L)',item.table_name,item.tenant_column);
                EXECUTE format('ALTER TABLE %s ENABLE ALWAYS TRIGGER act_file_source_change',item.table_name);
                EXECUTE format('CREATE TRIGGER act_file_source_truncate BEFORE TRUNCATE ON %s
                    FOR EACH STATEMENT EXECUTE FUNCTION cp6_act_file_v1.source_changed_v1()',item.table_name);
                EXECUTE format('ALTER TABLE %s ENABLE ALWAYS TRIGGER act_file_source_truncate',item.table_name);
            END LOOP;
        END $$;
        CREATE TRIGGER act_file_source_change AFTER INSERT OR UPDATE OR DELETE OR TRUNCATE ON public."Sys_Menus"
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_act_file_v1.source_changed_v1();
        ALTER TABLE public."Sys_Menus" ENABLE ALWAYS TRIGGER act_file_source_change;
        CREATE TRIGGER act_file_source_change AFTER UPDATE OR DELETE ON crm_identity."ServiceToken"
          FOR EACH ROW EXECUTE FUNCTION cp6_act_file_v1.source_changed_v1('TenantId');
        ALTER TABLE crm_identity."ServiceToken" ENABLE ALWAYS TRIGGER act_file_source_change;
        CREATE TRIGGER act_file_source_truncate BEFORE TRUNCATE ON crm_identity."ServiceToken"
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_act_file_v1.source_changed_v1();
        ALTER TABLE crm_identity."ServiceToken" ENABLE ALWAYS TRIGGER act_file_source_truncate;

        CREATE FUNCTION cp6_act_file_v1.grant_changed_v1() RETURNS trigger
        LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $$
        BEGIN
            PERFORM FROM cp6_act_file_v1.sources s WHERE s.tenant_id=NEW.tenant_id AND s.grant_login=session_user FOR UPDATE;
            IF NOT FOUND THEN
                RAISE EXCEPTION 'ACT_FILE_GRANT_AUTHORITY_UNAVAILABLE' USING ERRCODE='42501';
            END IF;
            IF TG_OP='UPDATE' AND ROW(OLD.tenant_id,OLD.actor_id,OLD.file_id,OLD.purpose)
                IS DISTINCT FROM ROW(NEW.tenant_id,NEW.actor_id,NEW.file_id,NEW.purpose) THEN
                RAISE EXCEPTION 'ACT_FILE_GRANT_IDENTITY_IMMUTABLE' USING ERRCODE='23514';
            END IF;
            IF EXISTS(SELECT FROM cp6_file_content_v1.dispositions d WHERE d.tenant_id=NEW.tenant_id
                AND d.file_id=NEW.file_id AND d.initial_state='DISPOSAL_PENDING') THEN
                RAISE EXCEPTION 'ACT_FILE_PHYSICAL_DISPOSITION_PENDING' USING ERRCODE='23514';
            END IF;
            PERFORM cp6_act_file_v1.advance_sources_v1(NEW.tenant_id);
            NEW.policy_version := gen_random_uuid();
            RETURN NEW;
        END $$;
        REVOKE ALL ON FUNCTION cp6_act_file_v1.grant_changed_v1() FROM PUBLIC;
        CREATE TRIGGER act_file_grant_change BEFORE INSERT OR UPDATE ON cp6_act_file_v1.grants
          FOR EACH ROW EXECUTE FUNCTION cp6_act_file_v1.grant_changed_v1();
        ALTER TABLE cp6_act_file_v1.grants ENABLE ALWAYS TRIGGER act_file_grant_change;
        CREATE TRIGGER act_file_grant_permanent BEFORE DELETE OR TRUNCATE ON cp6_act_file_v1.grants
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        ALTER TABLE cp6_act_file_v1.grants ENABLE ALWAYS TRIGGER act_file_grant_permanent;

        CREATE FUNCTION cp6_act_file_v1.protect_original_v1() RETURNS trigger
        LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $$
        BEGIN
            IF TG_TABLE_NAME='dispositions' AND to_jsonb(NEW)->>'initial_state'='UNLINKED' THEN RETURN NEW; END IF;
            PERFORM FROM cp6_act_file_v1.sources s WHERE s.tenant_id=NEW.tenant_id FOR UPDATE;
            IF EXISTS(SELECT FROM cp6_act_file_v1.grants g WHERE g.tenant_id=NEW.tenant_id AND g.file_id=NEW.file_id) THEN
                RAISE EXCEPTION 'ACT_FILE_REFERENCE_GRAPH_UNPROVEN' USING ERRCODE='23514';
            END IF;
            PERFORM cp6_act_file_v1.advance_sources_v1(NEW.tenant_id);
            RETURN NEW;
        END $$;
        REVOKE ALL ON FUNCTION cp6_act_file_v1.protect_original_v1() FROM PUBLIC;
        CREATE TRIGGER act_file_protect_original BEFORE INSERT ON cp6_file_content_v1.dispositions
          FOR EACH ROW EXECUTE FUNCTION cp6_act_file_v1.protect_original_v1();
        CREATE TRIGGER act_file_protect_original BEFORE INSERT ON cp6_file_content_v1.disposition_attempts
          FOR EACH ROW EXECUTE FUNCTION cp6_act_file_v1.protect_original_v1();
        ALTER TABLE cp6_file_content_v1.dispositions ENABLE ALWAYS TRIGGER act_file_protect_original;
        ALTER TABLE cp6_file_content_v1.disposition_attempts ENABLE ALWAYS TRIGGER act_file_protect_original;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("ACT 文件撤权代次和原回执只能前向恢复。");
}
