using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261119144500_GlAccountVersionWorkflowV1")]
public sealed class GlAccountVersionWorkflowV1 : Migration
{
    protected override void Up(MigrationBuilder migration) => migration.Sql("""
        CREATE SCHEMA cp6_fin_gl_v1;
        REVOKE ALL ON SCHEMA cp6_fin_gl_v1 FROM PUBLIC;
        CREATE TABLE cp6_fin_gl_v1.books (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL,
            scheme text COLLATE "C" NOT NULL CHECK (scheme ~ '^[A-Z0-9][A-Z0-9_.-]{0,19}$'),
            basis_key text COLLATE "C" NOT NULL CHECK (char_length(basis_key) BETWEEN 1 AND 200),
            roles text[] NOT NULL, currencies text[] NOT NULL, professional_available boolean NOT NULL,
            policy_from date NOT NULL, policy_to_exclusive date,
            CHECK (policy_to_exclusive IS NULL OR policy_from < policy_to_exclusive),
            policy_revision uuid NOT NULL DEFAULT gen_random_uuid(),
            scope_revision bigint NOT NULL DEFAULT 1 CHECK (scope_revision BETWEEN 1 AND 9007199254740991),
            PRIMARY KEY (tenant_id, ledger_id),
            FOREIGN KEY (tenant_id, ledger_id) REFERENCES cp6_fin_mfg_v1.ledgers
        );
        CREATE INDEX gl_book_legacy_scheme ON cp6_fin_gl_v1.books(tenant_id,scheme);
        CREATE FUNCTION cp6_fin_gl_v1.revise_policy() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF ROW(OLD.tenant_id,OLD.ledger_id,OLD.scheme) IS DISTINCT FROM ROW(NEW.tenant_id,NEW.ledger_id,NEW.scheme)
                THEN RAISE EXCEPTION 'GL_BOOK_IDENTITY_IMMUTABLE' USING ERRCODE='23514'; END IF;
            IF NEW.scope_revision NOT IN (OLD.scope_revision,OLD.scope_revision+1)
                THEN RAISE EXCEPTION 'GL_SCOPE_REVISION_INVALID' USING ERRCODE='23514'; END IF;
            IF ROW(OLD.basis_key,OLD.roles,OLD.currencies,OLD.professional_available,OLD.policy_from,OLD.policy_to_exclusive) IS DISTINCT FROM
               ROW(NEW.basis_key,NEW.roles,NEW.currencies,NEW.professional_available,NEW.policy_from,NEW.policy_to_exclusive)
                THEN NEW.policy_revision:=gen_random_uuid();
                ELSE NEW.policy_revision:=OLD.policy_revision;
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER gl_book_policy BEFORE UPDATE ON cp6_fin_gl_v1.books
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_gl_v1.revise_policy();
        CREATE TRIGGER gl_book_auth BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_fin_gl_v1.books
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();

        ALTER TABLE cp6_fin_mfg_v1.qualifications DROP CONSTRAINT qualifications_action_check;
        ALTER TABLE cp6_fin_mfg_v1.qualifications ADD CHECK (action IN ('rate-review','rate-release','center-review','center-release','basis-review','basis-release',
                'policy-review','policy-release','rate-basis-review','rate-basis-release','accounting-policy-review',
                'accounting-policy-release','source-contract-source-review','source-contract-gl-review','source-contract-release',
                'gl-account-review','gl-account-release'));
        ALTER TABLE cp6_fin_mfg_v1.qualification_decisions DROP CONSTRAINT qualification_decisions_action_check;
        ALTER TABLE cp6_fin_mfg_v1.qualification_decisions ADD CHECK (action IN ('rate-review','rate-release','center-review','center-release','basis-review','basis-release',
                'policy-review','policy-release','rate-basis-review','rate-basis-release','accounting-policy-review',
                'accounting-policy-release','source-contract-source-review','source-contract-gl-review','source-contract-release',
                'gl-account-review','gl-account-release'));
        CREATE TRIGGER fin_professional_qualification_fence BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
            ON cp6_fin_mfg_v1.qualifications FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER fin_professional_decision_fence BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
            ON cp6_fin_mfg_v1.qualification_decisions FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();

        -- 注册菜单操作供租户授权。现有角色由管理员显式分派。
        INSERT INTO public."Sys_MenuAction"("Id","TenantId","MenuId","ActionCode","ActionName","Sort","CreateDate")
        SELECT gen_random_uuid(),tenant."Id",menu."MenuId",action.code,action.name,action.sort,LOCALTIMESTAMP
        FROM public."Sys_Tenants" tenant
        JOIN public."Sys_Menus" menu ON menu."MenuKey"='fin-account'
        CROSS JOIN (VALUES ('view','读取会计科目',10),('add','创建科目草稿',20),('edit','维护科目草稿',30),
            ('submit','科目送审与撤回',40),('review','科目独立复核',50),('release','发布科目版本',60),
            ('deactivate','暂停科目',70),('restore','恢复科目',80),('retire','退休科目',90),
            ('history','读取科目历史与原操作',100),('import','导入科目草稿',110)) action(code,name,sort)
        WHERE NOT EXISTS(SELECT FROM public."Sys_MenuAction" old
            WHERE (old."TenantId",old."MenuId",old."ActionCode")=(tenant."Id",menu."MenuId",action.code));

        CREATE TABLE cp6_fin_gl_v1.roots (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, id uuid NOT NULL,
            code text COLLATE "C" NOT NULL CHECK (code ~ '^[A-Z0-9][A-Z0-9_.-]{0,29}$'),
            control_state text NOT NULL DEFAULT 'SUSPENDED' CHECK (control_state IN ('ENABLED','SUSPENDED','RETIRED')),
            control_revision bigint NOT NULL DEFAULT 1 CHECK (control_revision BETWEEN 1 AND 9007199254740991),
            timeline_revision bigint CHECK (timeline_revision BETWEEN 1 AND 9007199254740991),
            retired_on date, PRIMARY KEY(tenant_id,ledger_id,id), UNIQUE(tenant_id,ledger_id,code),
            FOREIGN KEY(tenant_id,ledger_id) REFERENCES cp6_fin_gl_v1.books,
            CHECK ((control_state='RETIRED')=(retired_on IS NOT NULL))
        );
        CREATE TABLE cp6_fin_gl_v1.versions (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, root_id uuid NOT NULL, id uuid NOT NULL,
            version_no bigint NOT NULL CHECK(version_no BETWEEN 1 AND 9007199254740991),
            maker uuid NOT NULL, payload text NOT NULL, digest text NOT NULL CHECK(digest ~ '^[0-9a-f]{64}$'),
            PRIMARY KEY(tenant_id,ledger_id,root_id,id),
            UNIQUE(tenant_id,ledger_id,root_id,version_no),
            FOREIGN KEY(tenant_id,ledger_id,root_id) REFERENCES cp6_fin_gl_v1.roots
        );
        CREATE TABLE cp6_fin_gl_v1.proposals (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, root_id uuid NOT NULL, id uuid NOT NULL,
            revision bigint NOT NULL DEFAULT 1 CHECK(revision BETWEEN 1 AND 9007199254740991),
            state text NOT NULL CHECK(state IN ('DRAFT','SUBMITTED','APPROVED','REJECTED','RELEASED','WITHDRAWN','CLOSED')),
            version_id uuid NOT NULL, makers uuid[] NOT NULL CHECK(cardinality(makers)>0),
            activate boolean NOT NULL, decision_id uuid, submission_basis uuid,
            CHECK ((state IN ('APPROVED','REJECTED','RELEASED'))=(decision_id IS NOT NULL)),
            PRIMARY KEY(tenant_id,ledger_id,root_id,id),
            FOREIGN KEY(tenant_id,ledger_id,root_id,version_id) REFERENCES cp6_fin_gl_v1.versions
        );
        CREATE UNIQUE INDEX gl_one_active_proposal ON cp6_fin_gl_v1.proposals(tenant_id,ledger_id,root_id)
            WHERE state IN ('DRAFT','SUBMITTED','APPROVED','REJECTED');
        CREATE TABLE cp6_fin_gl_v1.proposal_history (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, root_id uuid NOT NULL, proposal_id uuid NOT NULL,
            revision bigint NOT NULL, snapshot text NOT NULL, recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY(tenant_id,ledger_id,root_id,proposal_id,revision),
            FOREIGN KEY(tenant_id,ledger_id,root_id,proposal_id) REFERENCES cp6_fin_gl_v1.proposals
        );
        CREATE TABLE cp6_fin_gl_v1.decisions (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, root_id uuid NOT NULL, id uuid NOT NULL,
            kind text NOT NULL CHECK (kind IN ('APPROVE','REJECT','RESTORE')),
            reviewer uuid NOT NULL, snapshot text NOT NULL,
            PRIMARY KEY(tenant_id,ledger_id,root_id,id),
            FOREIGN KEY(tenant_id,ledger_id,root_id) REFERENCES cp6_fin_gl_v1.roots
        );
        ALTER TABLE cp6_fin_gl_v1.proposals ADD CONSTRAINT gl_proposal_decision_fk
            FOREIGN KEY(tenant_id,ledger_id,root_id,decision_id) REFERENCES cp6_fin_gl_v1.decisions;
        CREATE TABLE cp6_fin_gl_v1.timelines (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, root_id uuid NOT NULL, revision bigint NOT NULL,
            release_id uuid NOT NULL, version_id uuid NOT NULL, decision_id uuid NOT NULL, release_proof text NOT NULL,
            released_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY(tenant_id,ledger_id,root_id,revision), UNIQUE(tenant_id,ledger_id,root_id,release_id),
            UNIQUE(tenant_id,ledger_id,root_id,version_id),
            FOREIGN KEY(tenant_id,ledger_id,root_id,version_id) REFERENCES cp6_fin_gl_v1.versions,
            FOREIGN KEY(tenant_id,ledger_id,root_id,decision_id) REFERENCES cp6_fin_gl_v1.decisions
        );
        ALTER TABLE cp6_fin_gl_v1.roots ADD CONSTRAINT gl_current_timeline_fk
            FOREIGN KEY(tenant_id,ledger_id,id,timeline_revision) REFERENCES cp6_fin_gl_v1.timelines;
        CREATE TABLE cp6_fin_gl_v1.segments (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, root_id uuid NOT NULL, revision bigint NOT NULL,
            valid_from date NOT NULL, to_exclusive date, version_id uuid NOT NULL, release_id uuid NOT NULL,
            PRIMARY KEY(tenant_id,ledger_id,root_id,revision,valid_from),
            FOREIGN KEY(tenant_id,ledger_id,root_id,revision) REFERENCES cp6_fin_gl_v1.timelines,
            FOREIGN KEY(tenant_id,ledger_id,root_id,version_id) REFERENCES cp6_fin_gl_v1.versions,
            FOREIGN KEY(tenant_id,ledger_id,root_id,release_id) REFERENCES cp6_fin_gl_v1.timelines(tenant_id,ledger_id,root_id,release_id),
            CHECK (to_exclusive IS NULL OR valid_from < to_exclusive)
        );
        CREATE TRIGGER gl_segment_scope_fence BEFORE INSERT ON cp6_fin_gl_v1.segments
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE FUNCTION cp6_fin_gl_v1.require_disjoint() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF EXISTS(SELECT FROM cp6_fin_gl_v1.segments s
                WHERE (s.tenant_id,s.ledger_id,s.root_id,s.revision)=(NEW.tenant_id,NEW.ledger_id,NEW.root_id,NEW.revision)
                  AND daterange(s.valid_from,s.to_exclusive,'[)') && daterange(NEW.valid_from,NEW.to_exclusive,'[)'))
              THEN RAISE EXCEPTION 'GL_TIMELINE_OVERLAP' USING ERRCODE='23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER gl_segment_disjoint BEFORE INSERT ON cp6_fin_gl_v1.segments
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_gl_v1.require_disjoint();
        CREATE TABLE cp6_fin_gl_v1.previews (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, id uuid NOT NULL, subject_id uuid NOT NULL,
            kind text NOT NULL CHECK(kind IN ('PUBLISH','TEMPLATE')), snapshot text NOT NULL, expires_at timestamptz NOT NULL,
            PRIMARY KEY(tenant_id,ledger_id,id),
            FOREIGN KEY(tenant_id,ledger_id) REFERENCES cp6_fin_gl_v1.books
        );
        CREATE TABLE cp6_fin_gl_v1.consumed_previews (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, id uuid NOT NULL, operation_ref uuid NOT NULL,
            PRIMARY KEY(tenant_id,ledger_id,id),
            FOREIGN KEY(tenant_id,ledger_id,id) REFERENCES cp6_fin_gl_v1.previews
        );
        CREATE TABLE cp6_fin_gl_v1.events (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, operation_ref uuid NOT NULL,
            kind text NOT NULL, actor_id uuid NOT NULL, body text NOT NULL, recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY(tenant_id,ledger_id,operation_ref),
            FOREIGN KEY(tenant_id,ledger_id) REFERENCES cp6_fin_gl_v1.books
        );

        ALTER TABLE cp6_fin_gl_v1.consumed_previews ADD CONSTRAINT gl_consumption_operation_fk
            FOREIGN KEY(tenant_id,ledger_id,operation_ref) REFERENCES cp6_fin_gl_v1.events;
        CREATE TABLE cp6_fin_gl_v1.outbox (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, event_id uuid NOT NULL,
            scope_revision bigint NOT NULL CHECK(scope_revision BETWEEN 1 AND 9007199254740991),
            root_id uuid NOT NULL, body text NOT NULL, body_digest text NOT NULL CHECK(body_digest ~ '^[0-9a-f]{64}$'),
            route_key text NOT NULL DEFAULT 'finance.gl-account.refresh.v1' CHECK(route_key='finance.gl-account.refresh.v1'),
            state text NOT NULL DEFAULT 'PENDING' CHECK(state IN ('PENDING','CLAIMED','DELIVERED')),
            attempts integer NOT NULL DEFAULT 0 CHECK(attempts>=0),
            claim_id uuid, lease_until timestamptz, last_error_code text,
            PRIMARY KEY(tenant_id,ledger_id,event_id),
            FOREIGN KEY(tenant_id,ledger_id,event_id) REFERENCES cp6_fin_gl_v1.events,
            FOREIGN KEY(tenant_id,ledger_id,root_id) REFERENCES cp6_fin_gl_v1.roots,
            CHECK ((state='CLAIMED' AND claim_id IS NOT NULL AND lease_until IS NOT NULL)
                OR (state<>'CLAIMED' AND claim_id IS NULL AND lease_until IS NULL))
        );
        CREATE INDEX gl_outbox_work ON cp6_fin_gl_v1.outbox(state,lease_until,tenant_id,ledger_id,scope_revision);
        CREATE FUNCTION cp6_fin_gl_v1.guard_outbox() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF ROW(OLD.tenant_id,OLD.ledger_id,OLD.event_id,OLD.scope_revision,OLD.root_id,OLD.route_key,OLD.body,OLD.body_digest)
                IS DISTINCT FROM ROW(NEW.tenant_id,NEW.ledger_id,NEW.event_id,NEW.scope_revision,NEW.root_id,NEW.route_key,NEW.body,NEW.body_digest)
                THEN RAISE EXCEPTION 'GL_EVENT_BODY_IMMUTABLE' USING ERRCODE='23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER gl_outbox_identity BEFORE UPDATE ON cp6_fin_gl_v1.outbox
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_gl_v1.guard_outbox();
        CREATE TRIGGER gl_outbox_retention BEFORE DELETE OR TRUNCATE ON cp6_fin_gl_v1.outbox
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();

        DO $$
        DECLARE name text;
        BEGIN
            FOREACH name IN ARRAY ARRAY['versions','proposal_history','decisions','timelines','segments','previews','consumed_previews','events']
            LOOP
                EXECUTE format('CREATE TRIGGER gl_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_fin_gl_v1.%I
                    FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()',name);
            END LOOP;
        END $$;

        CREATE FUNCTION cp6_fin_gl_v1.guard_root() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF ROW(OLD.tenant_id,OLD.ledger_id,OLD.id,OLD.code) IS DISTINCT FROM ROW(NEW.tenant_id,NEW.ledger_id,NEW.id,NEW.code)
                OR OLD.control_state='RETIRED'
              THEN RAISE EXCEPTION 'GL_ROOT_IDENTITY_IMMUTABLE' USING ERRCODE='23514';
            END IF;
            IF (OLD.control_state=NEW.control_state AND OLD.control_revision<>NEW.control_revision)
              OR (OLD.control_state<>NEW.control_state AND NEW.control_revision<>OLD.control_revision+1)
              OR (OLD.timeline_revision IS DISTINCT FROM NEW.timeline_revision AND
                NEW.timeline_revision IS DISTINCT FROM COALESCE(OLD.timeline_revision,0)+1)
                THEN RAISE EXCEPTION 'GL_ROOT_REVISION_INVALID' USING ERRCODE='23514';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER gl_root_guard BEFORE UPDATE ON cp6_fin_gl_v1.roots
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_gl_v1.guard_root();
        CREATE TRIGGER gl_root_retention BEFORE DELETE OR TRUNCATE ON cp6_fin_gl_v1.roots
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();

        -- 旧科目缺法人和账套标识。任一已启用版本工作流的同租户同方案，旧写入均须走受控入口。
        CREATE FUNCTION cp6_fin_gl_v1.guard_legacy() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF (TG_OP <> 'INSERT' AND EXISTS(SELECT FROM cp6_fin_gl_v1.books b
                WHERE b.tenant_id=OLD."TenantId" AND b.scheme=upper(btrim(OLD."StandardScheme"::text) COLLATE "C")))
            OR (TG_OP <> 'DELETE' AND EXISTS(SELECT FROM cp6_fin_gl_v1.books b
                WHERE b.tenant_id=NEW."TenantId" AND b.scheme=upper(btrim(NEW."StandardScheme"::text) COLLATE "C")))
            THEN RAISE EXCEPTION 'GL_VERSION_WORKFLOW_REQUIRED' USING ERRCODE='23514'; END IF;
            IF TG_OP='DELETE' THEN RETURN OLD; END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER gl_legacy_scope_fence BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON public."Fin_GlAccount"
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER gl_legacy_row_guard BEFORE INSERT OR UPDATE OR DELETE ON public."Fin_GlAccount"
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_gl_v1.guard_legacy();
        CREATE FUNCTION cp6_fin_gl_v1.guard_legacy_truncate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF EXISTS(SELECT FROM public."Fin_GlAccount" a JOIN cp6_fin_gl_v1.books b
                ON b.tenant_id=a."TenantId" AND b.scheme=upper(btrim(a."StandardScheme"::text) COLLATE "C"))
            THEN RAISE EXCEPTION 'GL_VERSION_WORKFLOW_REQUIRED' USING ERRCODE='23514'; END IF;
            RETURN NULL;
        END $$;
        CREATE TRIGGER gl_legacy_truncate_guard BEFORE TRUNCATE ON public."Fin_GlAccount"
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_fin_gl_v1.guard_legacy_truncate();
        """);

    protected override void Down(MigrationBuilder migration)
        => throw new NotSupportedException("已发布的科目历史和原操作只允许前向迁移。");
}
