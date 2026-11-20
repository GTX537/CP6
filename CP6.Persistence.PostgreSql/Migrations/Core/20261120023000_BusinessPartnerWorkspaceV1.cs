using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261120023000_BusinessPartnerWorkspaceV1")]
public sealed class BusinessPartnerWorkspaceV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA main_bp;
        REVOKE ALL ON SCHEMA main_bp FROM PUBLIC;

        -- 经营主体及据点关系采用明确来源。已有行不按 BaseCd 猜法人或历史状态。
        CREATE TABLE main_bp.scopes (
            tenant_id uuid NOT NULL,
            legal_entity_key text COLLATE "C" NOT NULL CHECK (length(legal_entity_key) BETWEEN 1 AND 64),
            label text NOT NULL,
            enabled boolean NOT NULL,
            revision bigint NOT NULL CHECK (revision>0),
            norm_version text COLLATE "C" NOT NULL,
            coverage_complete boolean NOT NULL,
            PRIMARY KEY (tenant_id,legal_entity_key)
        );
        CREATE TABLE main_bp.bases (
            tenant_id uuid NOT NULL, legal_entity_key text COLLATE "C" NOT NULL, base_cd text COLLATE "C" NOT NULL,
            enabled boolean NOT NULL, source_version text COLLATE "C" NOT NULL,
            PRIMARY KEY (tenant_id,legal_entity_key,base_cd),
            UNIQUE (tenant_id,base_cd),
            FOREIGN KEY (tenant_id,legal_entity_key) REFERENCES main_bp.scopes
        );
        CREATE TABLE main_bp.base_grants (
            tenant_id uuid NOT NULL, legal_entity_key text COLLATE "C" NOT NULL, base_cd text COLLATE "C" NOT NULL,
            role_id integer NOT NULL, can_read boolean NOT NULL, can_manage boolean NOT NULL,
            CHECK (NOT can_manage OR can_read),
            PRIMARY KEY (tenant_id,legal_entity_key,base_cd,role_id),
            FOREIGN KEY (tenant_id,legal_entity_key,base_cd) REFERENCES main_bp.bases
        );
        CREATE TABLE main_bp.option_values (
            tenant_id uuid NOT NULL, legal_entity_key text COLLATE "C" NOT NULL,
            field text COLLATE "C" NOT NULL, code text COLLATE "C" NOT NULL, label text NOT NULL,
            owner text COLLATE "C" NOT NULL, source_id text COLLATE "C" NOT NULL,
            source_version text COLLATE "C" NOT NULL, source_digest text COLLATE "C" NOT NULL CHECK (source_digest ~ '^[0-9a-f]{64}$'),
            enabled boolean NOT NULL,
            PRIMARY KEY (tenant_id,legal_entity_key,field,code),
            FOREIGN KEY (tenant_id,legal_entity_key) REFERENCES main_bp.scopes
        );

        -- T_WebBusinessPartner 保持唯一业务根；资料的当前可写成员仍在其原列中。
        CREATE UNIQUE INDEX "IX_BusinessPartner_WorkspaceTenantIdentity" ON public."T_WebBusinessPartner"("TenantId","Id");
        CREATE TABLE main_bp.heads (
            tenant_id uuid NOT NULL, bp_id uuid NOT NULL, legal_entity_key text COLLATE "C" NOT NULL,
            registration text COLLATE "C" NOT NULL CHECK (registration IN ('PRE_REGISTERED','REGISTERED','UNKNOWN_LEGACY')),
            revision bigint NOT NULL CHECK (revision>0),
            PRIMARY KEY (tenant_id,bp_id),
            UNIQUE (tenant_id,legal_entity_key,bp_id),
            FOREIGN KEY (tenant_id,bp_id) REFERENCES public."T_WebBusinessPartner"("TenantId","Id"),
            FOREIGN KEY (tenant_id,legal_entity_key) REFERENCES main_bp.scopes
        );
        CREATE TABLE main_bp.profile_revisions (
            tenant_id uuid NOT NULL, legal_entity_key text COLLATE "C" NOT NULL, bp_id uuid NOT NULL,
            revision bigint NOT NULL CHECK (revision>0), row_version bytea NOT NULL CHECK (octet_length(row_version)=8),
            content_hash text COLLATE "C" NOT NULL CHECK (content_hash ~ '^[0-9a-f]{64}$'),
            protected_body bytea NOT NULL, recorded_at timestamptz NOT NULL,
            PRIMARY KEY (tenant_id,legal_entity_key,bp_id,revision),
            UNIQUE (tenant_id,bp_id,row_version),
            FOREIGN KEY (tenant_id,legal_entity_key,bp_id) REFERENCES main_bp.heads (tenant_id,legal_entity_key,bp_id)
        );
        ALTER TABLE main_bp.heads ADD CONSTRAINT partner_complete_revision
            FOREIGN KEY (tenant_id,legal_entity_key,bp_id,revision) REFERENCES main_bp.profile_revisions
            DEFERRABLE INITIALLY DEFERRED;
        CREATE TABLE main_bp.relations (
            tenant_id uuid NOT NULL, legal_entity_key text COLLATE "C" NOT NULL, bp_id uuid NOT NULL,
            slot text COLLATE "C" NOT NULL CHECK (slot IN ('CUSTOMER_AR','AR_BILLING','BILLING_RECEIPT','CUSTOMER_CREDIT',
                'BILLING_CREDIT_AR','PARENT_CUSTOMER','SUPPLIER_AP','AP_SCHEDULE','SCHEDULE_PAYEE')),
            target_id uuid,
            PRIMARY KEY (tenant_id,legal_entity_key,bp_id,slot),
            FOREIGN KEY (tenant_id,legal_entity_key,bp_id) REFERENCES main_bp.heads (tenant_id,legal_entity_key,bp_id),
            FOREIGN KEY (tenant_id,legal_entity_key,target_id) REFERENCES main_bp.heads (tenant_id,legal_entity_key,bp_id)
        );
        CREATE TABLE main_bp.normalized_identity (
            tenant_id uuid NOT NULL, legal_entity_key text COLLATE "C" NOT NULL, bp_id uuid NOT NULL,
            norm_version text COLLATE "C" NOT NULL, revision bigint NOT NULL,
            name_norm text COLLATE "C" NOT NULL, name_hash bytea NOT NULL CHECK (octet_length(name_hash)=32),
            standard_norm text COLLATE "C", standard_hash bytea, ein_type text COLLATE "C",
            ein_norm text COLLATE "C", ein_hash bytea,
            CHECK ((standard_norm IS NULL)=(standard_hash IS NULL) AND (ein_norm IS NULL)=(ein_hash IS NULL)),
            PRIMARY KEY (tenant_id,legal_entity_key,bp_id),
            FOREIGN KEY (tenant_id,legal_entity_key,bp_id,revision) REFERENCES main_bp.profile_revisions
        );
        CREATE INDEX partner_normalized_name ON main_bp.normalized_identity(tenant_id,legal_entity_key,norm_version,name_hash);
        CREATE INDEX partner_normalized_standard ON main_bp.normalized_identity(tenant_id,legal_entity_key,norm_version,standard_hash);
        CREATE INDEX partner_normalized_registration ON main_bp.normalized_identity(tenant_id,legal_entity_key,norm_version,ein_type,ein_hash);

        CREATE TABLE main_bp.commands (
            tenant_id uuid NOT NULL, command_id uuid NOT NULL, actor_id uuid NOT NULL,
            legal_entity_key text COLLATE "C" NOT NULL, bp_id uuid NOT NULL, operation text COLLATE "C" NOT NULL,
            fingerprint bytea NOT NULL CHECK (octet_length(fingerprint)=32), receipt text NOT NULL CHECK (jsonb_typeof(receipt::jsonb)='object'),
            PRIMARY KEY (tenant_id,command_id),
            FOREIGN KEY (tenant_id,legal_entity_key,bp_id) REFERENCES main_bp.heads (tenant_id,legal_entity_key,bp_id)
        );
        CREATE TABLE main_bp.previews (
            tenant_id uuid NOT NULL, preview_id uuid NOT NULL, actor_id uuid NOT NULL,
            legal_entity_key text COLLATE "C" NOT NULL, scope_revision bigint NOT NULL CHECK (scope_revision>0),
            operation text COLLATE "C" NOT NULL CHECK (operation IN ('CREATE','PROFILE_APPLY')),
            permission_version text COLLATE "C" NOT NULL, intent_hash text COLLATE "C" NOT NULL CHECK (intent_hash ~ '^[0-9a-f]{64}$'),
            source_hash text COLLATE "C" NOT NULL, candidate_hash text COLLATE "C" NOT NULL,
            protected_body bytea NOT NULL, bp_id uuid, row_version bytea,
            expires_at timestamptz NOT NULL, can_apply boolean NOT NULL, used_command uuid,
            PRIMARY KEY (tenant_id,preview_id),
            FOREIGN KEY (tenant_id,legal_entity_key) REFERENCES main_bp.scopes,
            FOREIGN KEY (tenant_id,legal_entity_key,bp_id) REFERENCES main_bp.heads (tenant_id,legal_entity_key,bp_id),
            FOREIGN KEY (tenant_id,used_command) REFERENCES main_bp.commands
        );
        CREATE TABLE main_bp.audit (
            tenant_id uuid NOT NULL, command_id uuid NOT NULL, legal_entity_key text COLLATE "C" NOT NULL,
            bp_id uuid NOT NULL, revision bigint NOT NULL, actor_id uuid NOT NULL,
            operation text COLLATE "C" NOT NULL, outcome text COLLATE "C" NOT NULL CHECK (outcome IN ('APPLIED','NO_CHANGE')),
            reason text, fields text[] NOT NULL, recorded_at timestamptz NOT NULL,
            PRIMARY KEY (tenant_id,command_id),
            FOREIGN KEY (tenant_id,command_id) REFERENCES main_bp.commands,
            FOREIGN KEY (tenant_id,legal_entity_key,bp_id,revision) REFERENCES main_bp.profile_revisions
        );
        CREATE TABLE main_bp.state_history (
            tenant_id uuid NOT NULL, legal_entity_key text COLLATE "C" NOT NULL, bp_id uuid NOT NULL,
            revision bigint NOT NULL, command_id uuid NOT NULL, registration text COLLATE "C" NOT NULL,
            lifecycle text COLLATE "C" NOT NULL CHECK (lifecycle IN ('ACTIVE','RETIRED')),
            hold text COLLATE "C" NOT NULL CHECK (hold IN ('CLEAR','FROZEN')),
            PRIMARY KEY (tenant_id,legal_entity_key,bp_id,revision),
            FOREIGN KEY (tenant_id,command_id) REFERENCES main_bp.audit,
            FOREIGN KEY (tenant_id,legal_entity_key,bp_id,revision) REFERENCES main_bp.profile_revisions
        );

        CREATE FUNCTION main_bp.protect_history() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            RAISE EXCEPTION 'BP_HISTORY_IMMUTABLE' USING ERRCODE='23514';
        END $$;
        DO $guard$
        DECLARE table_name text;
        BEGIN
            FOREACH table_name IN ARRAY ARRAY['profile_revisions','commands','audit','state_history'] LOOP
                EXECUTE format('CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON main_bp.%I FOR EACH ROW EXECUTE FUNCTION main_bp.protect_history()',table_name);
                EXECUTE format('CREATE TRIGGER immutable_truncate BEFORE TRUNCATE ON main_bp.%I FOR EACH STATEMENT EXECUTE FUNCTION main_bp.protect_history()',table_name);
            END LOOP;
        END $guard$;

        CREATE FUNCTION main_bp.guard_existing_root() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (SELECT FROM main_bp.heads WHERE tenant_id=OLD."TenantId" AND bp_id=OLD."Id") THEN
                IF TG_OP='DELETE' THEN RETURN OLD; END IF;
                RETURN NEW;
            END IF;
            IF TG_OP='DELETE' OR (OLD."TenantId",OLD."Id",OLD."BpCd") IS DISTINCT FROM (NEW."TenantId",NEW."Id",NEW."BpCd")
                OR current_setting('cp6.bp_workspace_writer',true) IS DISTINCT FROM NEW."Id"::text THEN
                RAISE EXCEPTION 'BP_WORKSPACE_WRITER_REQUIRED' USING ERRCODE='23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER workspace_owner BEFORE UPDATE OR DELETE ON public."T_WebBusinessPartner"
            FOR EACH ROW EXECUTE FUNCTION main_bp.guard_existing_root();
        REVOKE ALL ON ALL TABLES IN SCHEMA main_bp FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA main_bp FROM PUBLIC;

        INSERT INTO public."Sys_MenuAction" ("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",actions.code,actions.label,0,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        CROSS JOIN (VALUES ('status','业务伙伴状态操作'),('register','正式注册'),('duplicate-review','核对重复候选'),
           ('command-result.read','回查业务伙伴原命令'),('history','读取业务伙伴历史'),
           ('sensitive.read','读取业务伙伴敏感资料'),('sensitive.write','维护业务伙伴敏感资料')) actions(code,label)
        WHERE m."MenuKey"='erp-business-partner' AND NOT EXISTS (
            SELECT FROM public."Sys_MenuAction" a WHERE a."TenantId"=t."Id" AND a."MenuId"=m."MenuId" AND a."ActionCode"=actions.code
        );
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("交易伙伴不可变版本只支持前向迁移。");
}
