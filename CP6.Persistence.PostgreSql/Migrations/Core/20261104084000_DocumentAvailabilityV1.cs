using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261104084000_DocumentAvailabilityV1")]
public sealed class DocumentAvailabilityV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        LOCK TABLE cp6_controlled_document_v1.heads,cp6_controlled_document_v1.versions IN SHARE ROW EXCLUSIVE MODE;
        CREATE TABLE cp6_controlled_document_v1.availability(
          tenant_id uuid NOT NULL,document_id uuid NOT NULL,revision bigint NOT NULL,state_version bigint NOT NULL CHECK(state_version>0),
          etag uuid NOT NULL CHECK(etag<>'00000000-0000-0000-0000-000000000000'::uuid),
          state text NOT NULL CHECK(state IN('AVAILABLE','HELD','WITHDRAWN')),
          action text NOT NULL CHECK(action IN('REGISTER','HOLD','RELEASE','WITHDRAW')),reason text NOT NULL,
          actor_id uuid NOT NULL,recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),operation_id uuid,
          owner text NOT NULL DEFAULT 'Main.Plm.ControlledDocument.v1' CHECK(owner='Main.Plm.ControlledDocument.v1'),
          PRIMARY KEY(tenant_id,document_id,revision,state_version),UNIQUE(tenant_id,operation_id),
          FOREIGN KEY(tenant_id,document_id,revision) REFERENCES cp6_controlled_document_v1.versions,
          FOREIGN KEY(tenant_id,owner,operation_id) REFERENCES cp6_main_owner_v1.operations DEFERRABLE INITIALLY DEFERRED);
        -- Establish a separate usage control for existing originals; this grants no file access or professional approval.
        INSERT INTO cp6_controlled_document_v1.availability(tenant_id,document_id,revision,state_version,etag,state,action,reason,actor_id,recorded_at)
          SELECT tenant_id,document_id,revision,1,(convert_from(body_bytes,'UTF8')::jsonb->>'etag')::uuid,
            'AVAILABLE','REGISTER','Initial document usage control; file and approval requirements remain independent',actor_id,recorded_at
          FROM cp6_controlled_document_v1.versions;
        CREATE FUNCTION cp6_controlled_document_v1.register_availability() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          INSERT INTO cp6_controlled_document_v1.availability(tenant_id,document_id,revision,state_version,etag,state,action,reason,actor_id)
            VALUES(NEW.tenant_id,NEW.document_id,NEW.revision,1,(convert_from(NEW.body_bytes,'UTF8')::jsonb->>'etag')::uuid,
              'AVAILABLE','REGISTER','Initial document usage control; file and approval requirements remain independent',NEW.actor_id);
          RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_controlled_document_v1.validate_availability() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE previous cp6_controlled_document_v1.availability; BEGIN
          SELECT * INTO previous FROM cp6_controlled_document_v1.availability
            WHERE tenant_id=NEW.tenant_id AND document_id=NEW.document_id AND revision=NEW.revision ORDER BY state_version DESC LIMIT 1;
          IF previous.state_version IS NULL THEN
            IF NEW.state_version<>1 OR NEW.state<>'AVAILABLE' OR NEW.action<>'REGISTER' OR NEW.operation_id IS NOT NULL
              OR NOT EXISTS(SELECT FROM cp6_controlled_document_v1.versions v WHERE v.tenant_id=NEW.tenant_id AND v.document_id=NEW.document_id
                AND v.revision=NEW.revision AND v.actor_id=NEW.actor_id AND (convert_from(v.body_bytes,'UTF8')::jsonb->>'etag')::uuid=NEW.etag)
              THEN RAISE EXCEPTION 'DOCUMENT_AVAILABILITY_INITIAL_INVALID'; END IF;
          ELSE
            IF NEW.state_version<>previous.state_version+1 OR NEW.etag=previous.etag OR NEW.operation_id IS NULL
              OR btrim(NEW.reason)='' OR NOT ((previous.state='AVAILABLE' AND NEW.action='HOLD' AND NEW.state='HELD')
                OR (previous.state='HELD' AND NEW.action='RELEASE' AND NEW.state='AVAILABLE')
                OR (previous.state IN('AVAILABLE','HELD') AND NEW.action='WITHDRAW' AND NEW.state='WITHDRAWN'))
              THEN RAISE EXCEPTION 'DOCUMENT_AVAILABILITY_TRANSITION_INVALID'; END IF;
          END IF; RETURN NEW;
        END $$;
        CREATE TRIGGER document_availability_register AFTER INSERT ON cp6_controlled_document_v1.versions
          FOR EACH ROW EXECUTE FUNCTION cp6_controlled_document_v1.register_availability();
        CREATE TRIGGER document_availability_validate BEFORE INSERT ON cp6_controlled_document_v1.availability
          FOR EACH ROW EXECUTE FUNCTION cp6_controlled_document_v1.validate_availability();
        CREATE TRIGGER document_availability_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_controlled_document_v1.availability
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE FUNCTION cp6_controlled_document_v1.availability_receipt() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE op cp6_main_owner_v1.operations;b jsonb;r jsonb;p cp6_controlled_document_v1.availability; BEGIN
          IF NEW.operation_id IS NULL THEN RETURN NULL; END IF;
          SELECT * INTO op FROM cp6_main_owner_v1.operations WHERE tenant_id=NEW.tenant_id AND owner=NEW.owner AND operation_id=NEW.operation_id;
          b=convert_from(op.input_bytes,'UTF8')::jsonb;r=convert_from(op.result_bytes,'UTF8')::jsonb;
          SELECT * INTO p FROM cp6_controlled_document_v1.availability WHERE tenant_id=NEW.tenant_id AND document_id=NEW.document_id
            AND revision=NEW.revision AND state_version=NEW.state_version-1;
          IF op.state IS DISTINCT FROM 'COMMITTED' OR op.actor_id IS DISTINCT FROM NEW.actor_id
            OR op.contract IS DISTINCT FROM 'Main.Plm.ControlledDocument.commands.v1'
            OR b->>'OperationId' IS DISTINCT FROM NEW.operation_id::text OR b#>>'{Document,Id}' IS DISTINCT FROM NEW.document_id::text
            OR b#>>'{Document,Version}' IS DISTINCT FROM NEW.revision::text OR b->>'Action' IS DISTINCT FROM NEW.action
            OR b->>'Reason' IS DISTINCT FROM NEW.reason OR b->>'ExpectedStateVersion' IS DISTINCT FROM p.state_version::text
            OR b->>'ExpectedEtag' IS DISTINCT FROM p.etag::text OR r#>>'{Availability,StateVersion}' IS DISTINCT FROM NEW.state_version::text
            OR r#>>'{Availability,Etag}' IS DISTINCT FROM NEW.etag::text OR r#>>'{Availability,State}' IS DISTINCT FROM NEW.state
            OR r->>'OperationId' IS DISTINCT FROM NEW.operation_id::text OR r->>'ObjectId' IS DISTINCT FROM NEW.document_id::text
            OR r->>'Version' IS DISTINCT FROM NEW.revision::text OR NOT EXISTS(SELECT FROM cp6_controlled_document_v1.versions v
              WHERE v.tenant_id=NEW.tenant_id AND v.document_id=NEW.document_id AND v.revision=NEW.revision
                AND v.body_digest=b#>>'{Document,Digest}' AND v.body_digest=r->>'Digest')
            THEN RAISE EXCEPTION 'DOCUMENT_AVAILABILITY_RECEIPT_INVALID'; END IF; RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER document_availability_receipt AFTER INSERT ON cp6_controlled_document_v1.availability
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_controlled_document_v1.availability_receipt();
        CREATE FUNCTION cp6_controlled_document_v1.availability_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE b jsonb; BEGIN
          IF NEW.owner<>'Main.Plm.ControlledDocument.v1' OR NEW.contract<>'Main.Plm.ControlledDocument.commands.v1'
            OR NEW.state<>'COMMITTED' THEN RETURN NULL; END IF;
          b=convert_from(NEW.input_bytes,'UTF8')::jsonb;
          IF b->>'Action' IN('HOLD','RELEASE','WITHDRAW') AND NOT EXISTS(SELECT FROM cp6_controlled_document_v1.availability a
            WHERE a.tenant_id=NEW.tenant_id AND a.operation_id=NEW.operation_id AND a.actor_id=NEW.actor_id
              AND a.document_id::text=b#>>'{Document,Id}' AND a.revision::text=b#>>'{Document,Version}' AND a.action=b->>'Action')
            THEN RAISE EXCEPTION 'DOCUMENT_AVAILABILITY_EFFECT_MISSING'; END IF; RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER document_availability_complete AFTER INSERT ON cp6_main_owner_v1.operations
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_controlled_document_v1.availability_complete();
        CREATE FUNCTION cp6_controlled_document_v1.availability_ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT NOT EXISTS(SELECT FROM (VALUES
            ('versions','document_availability_register','cp6_controlled_document_v1.register_availability()',5),
            ('availability','document_availability_validate','cp6_controlled_document_v1.validate_availability()',7),
            ('availability','document_availability_immutable','cp6_scope_authorization_v1.immutable()',58),
            ('availability','document_availability_receipt','cp6_controlled_document_v1.availability_receipt()',5)) a(tbl,name,fn,typ)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_controlled_document_v1.'||a.tbl)
              AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL
              AND (a.name<>'document_availability_receipt' OR t.tgdeferrable AND t.tginitdeferred)))
          AND EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid='cp6_main_owner_v1.operations'::regclass
            AND t.tgname='document_availability_complete' AND t.tgfoid='cp6_controlled_document_v1.availability_complete()'::regprocedure
            AND t.tgtype=5 AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND t.tgdeferrable AND t.tginitdeferred);
        $$;
        REVOKE ALL ON cp6_controlled_document_v1.availability FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_controlled_document_v1 FROM PUBLIC;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
          SELECT gen_random_uuid(),m."MenuId",'availability','管理修订可用性',20,t."Id",LOCALTIMESTAMP
          FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t WHERE m."MenuKey"='plm-controlled-document'
          AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" a WHERE a."MenuId"=m."MenuId" AND a."TenantId"=t."Id" AND a."ActionCode"='availability');
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Document availability facts require forward recovery.");
}
