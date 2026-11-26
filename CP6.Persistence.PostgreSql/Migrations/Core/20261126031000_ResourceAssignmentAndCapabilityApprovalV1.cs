using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261126031000_ResourceAssignmentAndCapabilityApprovalV1")]
public sealed class ResourceAssignmentAndCapabilityApprovalV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_resource_assignment_v1;
        REVOKE ALL ON SCHEMA cp6_resource_assignment_v1 FROM PUBLIC;
        CREATE TABLE cp6_resource_assignment_v1.heads(
          tenant_id uuid NOT NULL,machine_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          PRIMARY KEY(tenant_id,machine_id));
        CREATE TABLE cp6_resource_assignment_v1.versions(
          tenant_id uuid NOT NULL,machine_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          machine_revision bigint NOT NULL,operation_id uuid NOT NULL,actor_id uuid NOT NULL,
          body bytea NOT NULL CHECK(octet_length(body) BETWEEN 2 AND 1048576),
          digest text NOT NULL CHECK(digest=encode(sha256(body),'hex')),
          machine_kind text GENERATED ALWAYS AS ('MACHINE'::text) STORED,
          journal text GENERATED ALWAYS AS ('Main.Mes.ResourceAssignment.v1'::text) STORED,
          PRIMARY KEY(tenant_id,machine_id,revision),UNIQUE(tenant_id,journal,operation_id),
          FOREIGN KEY(tenant_id,machine_id) REFERENCES cp6_resource_assignment_v1.heads,
          FOREIGN KEY(tenant_id,machine_kind,machine_id,machine_revision)
            REFERENCES cp6_resource_source_v1.versions(tenant_id,kind,resource_id,revision),
          FOREIGN KEY(tenant_id,journal,operation_id)
            REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED);
        ALTER TABLE cp6_resource_assignment_v1.heads ADD CONSTRAINT resource_assignment_head_original
          FOREIGN KEY(tenant_id,machine_id,revision) REFERENCES cp6_resource_assignment_v1.versions DEFERRABLE INITIALLY DEFERRED;

        CREATE FUNCTION cp6_resource_assignment_v1.advance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.revision<>1 THEN RAISE EXCEPTION 'RESOURCE_ASSIGNMENT_FRONTIER_INVALID'; END IF;
          ELSIF (NEW.tenant_id,NEW.machine_id) IS DISTINCT FROM (OLD.tenant_id,OLD.machine_id)
            OR NEW.revision<>OLD.revision+1 THEN RAISE EXCEPTION 'RESOURCE_ASSIGNMENT_FRONTIER_INVALID'; END IF;
          RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_resource_assignment_v1.complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE p jsonb=convert_from(NEW.body,'UTF8')::jsonb; item jsonb; result jsonb; original cp6_main_owner_v1.operations;
          machine_digest text;
        BEGIN
          SELECT v.source_digest INTO machine_digest FROM cp6_resource_source_v1.versions v
            WHERE v.tenant_id=NEW.tenant_id AND v.kind='MACHINE' AND v.resource_id=NEW.machine_id AND v.revision=NEW.machine_revision;
          IF p->>'tenantId' IS DISTINCT FROM NEW.tenant_id::text OR p->>'machineId' IS DISTINCT FROM NEW.machine_id::text
            OR p->>'version' IS DISTINCT FROM NEW.revision::text OR p->>'actorId' IS DISTINCT FROM NEW.actor_id::text
            OR p#>>'{plan,machine,owner}' IS DISTINCT FROM 'Main.Mes.ResourceSource.v1'
            OR p#>>'{plan,machine,kind}' IS DISTINCT FROM 'MACHINE'
            OR p#>>'{plan,machine,id}' IS DISTINCT FROM NEW.machine_id::text
            OR p#>>'{plan,machine,version}' IS DISTINCT FROM NEW.machine_revision::text
            OR p#>>'{plan,machine,digest}' IS DISTINCT FROM machine_digest
            OR jsonb_typeof(p#>'{plan,intervals}') IS DISTINCT FROM 'array'
            OR NEW.revision>1 AND NOT EXISTS(SELECT FROM cp6_resource_assignment_v1.versions v
              WHERE v.tenant_id=NEW.tenant_id AND v.machine_id=NEW.machine_id AND v.revision=NEW.revision-1)
            THEN RAISE EXCEPTION 'RESOURCE_ASSIGNMENT_ORIGINAL_INVALID'; END IF;
          FOR item IN SELECT value FROM jsonb_array_elements(p#>'{plan,intervals}') LOOP
            IF NOT EXISTS(SELECT FROM cp6_resource_source_v1.versions v
              WHERE v.tenant_id=NEW.tenant_id AND v.kind='WORK_CENTER'
                AND v.resource_id=(item#>>'{workCenter,id}')::uuid AND v.revision=(item#>>'{workCenter,version}')::bigint
                AND v.source_digest=item#>>'{workCenter,digest}' AND item#>>'{workCenter,kind}'='WORK_CENTER'
                AND item#>>'{workCenter,owner}'='Main.Mes.ResourceSource.v1')
              THEN RAISE EXCEPTION 'RESOURCE_ASSIGNMENT_WORK_CENTER_ORIGINAL_REQUIRED'; END IF;
          END LOOP;
          SELECT * INTO original FROM cp6_main_owner_v1.operations
            WHERE tenant_id=NEW.tenant_id AND owner=NEW.journal AND operation_id=NEW.operation_id;
          result=convert_from(original.result_bytes,'UTF8')::jsonb;
          IF original.actor_id IS DISTINCT FROM NEW.actor_id OR original.contract IS DISTINCT FROM 'Main.Mes.ResourceAssignment.commands.v1'
            OR original.state IS DISTINCT FROM 'COMMITTED' OR original.object_ids IS DISTINCT FROM ARRAY[NEW.machine_id]
            OR result->>'OperationId' IS DISTINCT FROM NEW.operation_id::text OR result->>'Kind' IS DISTINCT FROM 'MACHINE'
            OR result->>'ObjectId' IS DISTINCT FROM NEW.machine_id::text OR result->>'Version' IS DISTINCT FROM NEW.revision::text
            OR result->>'Digest' IS DISTINCT FROM NEW.digest
            THEN RAISE EXCEPTION 'RESOURCE_ASSIGNMENT_OPERATION_REQUIRED'; END IF;
          RETURN NEW;
        END $$;

        CREATE TRIGGER resource_assignment_advance BEFORE INSERT OR UPDATE ON cp6_resource_assignment_v1.heads
          FOR EACH ROW EXECUTE FUNCTION cp6_resource_assignment_v1.advance();
        CREATE TRIGGER resource_assignment_keep BEFORE DELETE OR TRUNCATE ON cp6_resource_assignment_v1.heads
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER resource_assignment_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_resource_assignment_v1.versions
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER resource_assignment_root BEFORE INSERT OR UPDATE OR DELETE ON cp6_resource_assignment_v1.heads
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER resource_assignment_root BEFORE INSERT ON cp6_resource_assignment_v1.versions
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE CONSTRAINT TRIGGER resource_assignment_complete AFTER INSERT ON cp6_resource_assignment_v1.versions
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_resource_assignment_v1.complete();
        CREATE FUNCTION cp6_resource_assignment_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT NOT EXISTS(SELECT FROM (VALUES
            ('heads','resource_assignment_advance','cp6_resource_assignment_v1.advance()',23),
            ('heads','resource_assignment_keep','cp6_scope_authorization_v1.immutable()',42),
            ('heads','resource_assignment_root','cp6_authentication_v1.before_change()',30),
            ('versions','resource_assignment_immutable','cp6_scope_authorization_v1.immutable()',58),
            ('versions','resource_assignment_complete','cp6_resource_assignment_v1.complete()',5),
            ('versions','resource_assignment_root','cp6_authentication_v1.before_change()',6)) a(tbl,name,fn,typ)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_resource_assignment_v1.'||a.tbl) AND t.tgname=a.name
              AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL
              AND (a.name<>'resource_assignment_complete' OR t.tgdeferrable AND t.tginitdeferred)));
        $$;

        CREATE FUNCTION cp6_resource_capability_v1.approval_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE p jsonb=convert_from(NEW.body,'UTF8')::jsonb; declaration jsonb; declaration_digest text; approval_audit jsonb;
        BEGIN
          IF NOT p?'approval' THEN RETURN NEW; END IF;
          SELECT convert_from(v.body,'UTF8')::jsonb,v.digest INTO declaration,declaration_digest FROM cp6_resource_capability_v1.versions v
            WHERE v.tenant_id=NEW.tenant_id AND v.kind=NEW.kind AND v.resource_id=NEW.resource_id AND v.revision=NEW.revision-1;
          SELECT j.audit_json::jsonb INTO approval_audit FROM cp6_main_owner_v1.operations j
            WHERE j.tenant_id=NEW.tenant_id AND j.owner=NEW.journal AND j.operation_id=NEW.operation_id;
          IF NEW.kind<>'MACHINE' OR declaration IS NULL OR declaration?'approval'
            OR p->'body' IS DISTINCT FROM declaration->'body'
            OR p#>>'{approval,operationId}' IS DISTINCT FROM NEW.operation_id::text
            OR p#>>'{approval,declaration,owner}' IS DISTINCT FROM 'Main.Mes.ResourceCapability.v1'
            OR p#>>'{approval,declaration,kind}' IS DISTINCT FROM NEW.kind
            OR p#>>'{approval,declaration,id}' IS DISTINCT FROM NEW.resource_id::text
            OR p#>>'{approval,declaration,version}' IS DISTINCT FROM (NEW.revision-1)::text
            OR p#>>'{approval,declaration,digest}' IS DISTINCT FROM declaration_digest
            OR nullif(p#>>'{approval,evidence}','') IS NULL
            OR approval_audit->>'action' IS DISTINCT FROM 'approve-resource-capability'
            THEN RAISE EXCEPTION 'RESOURCE_CAPABILITY_APPROVAL_ORIGINAL_REQUIRED'; END IF;
          RETURN NEW;
        END $$;
        CREATE CONSTRAINT TRIGGER resource_capability_approval_complete AFTER INSERT ON cp6_resource_capability_v1.versions
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_resource_capability_v1.approval_complete();
        CREATE FUNCTION cp6_resource_capability_v1.approval_ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_resource_capability_v1.versions')
            AND t.tgname='resource_capability_approval_complete'
            AND t.tgfoid=to_regprocedure('cp6_resource_capability_v1.approval_complete()')
            AND t.tgtype=5 AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND t.tgdeferrable AND t.tginitdeferred);
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_resource_assignment_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_resource_assignment_v1 FROM PUBLIC;
        REVOKE ALL ON FUNCTION cp6_resource_capability_v1.approval_complete() FROM PUBLIC;
        REVOKE ALL ON FUNCTION cp6_resource_capability_v1.approval_ready() FROM PUBLIC;

        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.label,a.sort,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
          CROSS JOIN (VALUES ('assignment-edit','登记机器归属',11),('capability-approve','批准资源能力',12)) a(code,label,sort)
        WHERE m."MenuKey"='mes-machine' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" current_action
          WHERE current_action."TenantId"=t."Id" AND current_action."MenuId"=m."MenuId" AND current_action."ActionCode"=a.code);
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("资源归属和能力批准原件需要前滚恢复。");
}
