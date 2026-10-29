using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261030021000_LocationOperationalControlV1")]
public sealed class LocationOperationalControlV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE SCHEMA cp6_location_control_v1;
        REVOKE ALL ON SCHEMA cp6_location_control_v1 FROM PUBLIC;
        CREATE TABLE cp6_location_control_v1.controls(
          tenant_id uuid NOT NULL,location_id uuid PRIMARY KEY REFERENCES public."T_Location"("Id"),
          revision bigint NOT NULL CHECK(revision>0),other_blocked boolean NOT NULL,UNIQUE(tenant_id,location_id));
        CREATE TABLE cp6_location_control_v1.events(
          tenant_id uuid NOT NULL,location_id uuid NOT NULL,id uuid PRIMARY KEY,block_id uuid NOT NULL,
          kind text NOT NULL CHECK(kind IN('BLOCK','RELEASE_DECISION','RELEASE')),revision_before bigint NOT NULL CHECK(revision_before>0),
          operation_id uuid NOT NULL,actor_id uuid NOT NULL,body bytea NOT NULL,digest text NOT NULL CHECK(digest~'^[0-9a-f]{64}$'),
          UNIQUE(tenant_id,id),UNIQUE(tenant_id,operation_id),
          FOREIGN KEY(tenant_id,location_id) REFERENCES cp6_location_control_v1.controls(tenant_id,location_id));
        CREATE UNIQUE INDEX one_release ON cp6_location_control_v1.events(tenant_id,block_id) WHERE kind='RELEASE';
        CREATE INDEX location_events ON cp6_location_control_v1.events(tenant_id,location_id,id);
        CREATE FUNCTION cp6_location_control_v1.head_original() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF NEW.revision<>1 OR NOT EXISTS(SELECT FROM public."T_Location" n WHERE n."Id"=NEW.location_id AND n."TenantId"=NEW.tenant_id)
            OR NOT EXISTS(SELECT FROM cp6_location_control_v1.events e WHERE e.tenant_id=NEW.tenant_id AND e.location_id=NEW.location_id
              AND e.kind='BLOCK' AND e.revision_before=1 AND e.xmin=pg_current_xact_id()::text::xid)
            THEN RAISE EXCEPTION 'LOCATION_CONTROL_ORIGINAL_REQUIRED' USING ERRCODE='23514';END IF;RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER control_head_original AFTER INSERT ON cp6_location_control_v1.controls DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_location_control_v1.head_original();
        CREATE FUNCTION cp6_location_control_v1.open_blocks(t uuid,l uuid) RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT EXISTS(SELECT FROM cp6_location_control_v1.events b WHERE b.tenant_id=t AND b.location_id=l AND b.kind='BLOCK'
            AND NOT EXISTS(SELECT FROM cp6_location_control_v1.events r WHERE r.tenant_id=t AND r.location_id=l AND r.block_id=b.id AND r.kind='RELEASE'))
        $$;
        CREATE FUNCTION cp6_location_control_v1.head_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='UPDATE' AND pg_trigger_depth()>1 AND NEW.tenant_id=OLD.tenant_id AND NEW.location_id=OLD.location_id
            AND NEW.revision=OLD.revision+1 THEN RETURN NEW;END IF;
          RAISE EXCEPTION 'LOCATION_CONTROL_IMMUTABLE' USING ERRCODE='23514';
        END $$;
        CREATE TRIGGER control_head_guard BEFORE UPDATE OR DELETE ON cp6_location_control_v1.controls FOR EACH ROW EXECUTE FUNCTION cp6_location_control_v1.head_guard();
        CREATE TRIGGER control_head_truncate BEFORE TRUNCATE ON cp6_location_control_v1.controls FOR EACH STATEMENT EXECUTE FUNCTION cp6_location_control_v1.head_guard();
        ALTER TABLE cp6_location_draft_v1.locations DROP CONSTRAINT locations_control_revision_check;
        ALTER TABLE cp6_location_draft_v1.locations ADD CONSTRAINT locations_control_revision_check CHECK(control_revision>0);
        CREATE OR REPLACE FUNCTION cp6_location_draft_v1.protect() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='UPDATE' AND pg_trigger_depth()>1 AND
            (((to_jsonb(NEW)-'business_revision')=(to_jsonb(OLD)-'business_revision') AND NEW.business_revision=OLD.business_revision+1)
              OR ((to_jsonb(NEW)-'control_revision')=(to_jsonb(OLD)-'control_revision') AND NEW.control_revision=OLD.control_revision+1))
            THEN RETURN NEW;END IF;
          RAISE EXCEPTION 'LOCATION_INITIALIZATION_IMMUTABLE' USING ERRCODE='23514';
        END $$;
        CREATE FUNCTION cp6_location_control_v1.draft_revision() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN UPDATE cp6_location_draft_v1.locations SET control_revision=NEW.revision
          WHERE tenant_id=NEW.tenant_id AND id=NEW.location_id;RETURN NULL;END $$;
        CREATE TRIGGER control_draft_revision AFTER UPDATE ON cp6_location_control_v1.controls FOR EACH ROW EXECUTE FUNCTION cp6_location_control_v1.draft_revision();
        CREATE TRIGGER control_events_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_location_control_v1.events FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE FUNCTION cp6_location_control_v1.apply_event() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE h cp6_location_control_v1.controls%ROWTYPE; b jsonb;desired boolean;
        BEGIN
          SELECT * INTO STRICT h FROM cp6_location_control_v1.controls WHERE tenant_id=NEW.tenant_id AND location_id=NEW.location_id FOR UPDATE;
          IF h.revision<>NEW.revision_before THEN RAISE EXCEPTION 'LOCATION_CONTROL_PREDECESSOR_CHANGED' USING ERRCODE='23514';END IF;
          b=convert_from(NEW.body,'UTF8')::jsonb;
          IF encode(sha256(NEW.body),'hex')<>NEW.digest OR NOT b @> jsonb_build_object('tenantId',NEW.tenant_id::text,
            'locationId',NEW.location_id::text,'id',NEW.id::text,'blockId',NEW.block_id::text,'kind',NEW.kind,
            'operationId',NEW.operation_id::text,'actorId',NEW.actor_id::text,'controlBefore',NEW.revision_before::text)
            OR NEW.kind='BLOCK' AND NEW.id<>NEW.block_id THEN RAISE EXCEPTION 'LOCATION_CONTROL_ORIGINAL_INVALID' USING ERRCODE='23514';END IF;
          IF NEW.kind<>'BLOCK' AND NOT EXISTS(SELECT FROM cp6_location_control_v1.events e
            WHERE e.tenant_id=NEW.tenant_id AND e.location_id=NEW.location_id AND e.id=NEW.block_id AND e.kind='BLOCK')
            THEN RAISE EXCEPTION 'LOCATION_BLOCK_REQUIRED' USING ERRCODE='23514';END IF;
          IF NEW.kind='RELEASE' AND NOT EXISTS(SELECT FROM cp6_location_control_v1.events e
            WHERE e.tenant_id=NEW.tenant_id AND e.location_id=NEW.location_id AND e.block_id=NEW.block_id AND e.kind='RELEASE_DECISION'
            AND e.id::text=b->'decisionRef'->>'id' AND e.digest=b->'decisionRef'->>'digest'
            AND e.actor_id=NEW.actor_id AND e.revision_before=NEW.revision_before
            AND convert_from(e.body,'UTF8')::jsonb->>'nativeEtag'=b->>'nativeEtag')
            THEN RAISE EXCEPTION 'LOCATION_RELEASE_BASIS_REQUIRED' USING ERRCODE='23514';END IF;
          IF NEW.kind<>'RELEASE_DECISION' THEN
            UPDATE cp6_location_control_v1.controls SET revision=revision+1 WHERE location_id=NEW.location_id;
            desired=h.other_blocked OR cp6_location_control_v1.open_blocks(NEW.tenant_id,NEW.location_id);
            UPDATE public."T_Location" SET "IsBlocked"=desired OR "IsInitializationQuarantined"
              WHERE "TenantId"=NEW.tenant_id AND "Id"=NEW.location_id AND "IsBlocked" IS DISTINCT FROM (desired OR "IsInitializationQuarantined");
          END IF;
          RETURN NULL;
        END $$;
        CREATE TRIGGER control_event_apply AFTER INSERT ON cp6_location_control_v1.events FOR EACH ROW EXECUTE FUNCTION cp6_location_control_v1.apply_event();
        CREATE FUNCTION cp6_location_control_v1.original() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF NOT EXISTS(SELECT FROM cp6_main_owner_v1.operations o WHERE o.tenant_id=NEW.tenant_id
            AND o.owner='Main.Wms.LocationOperationalControl.v1' AND o.contract='LOCATION-LOCAL-CONTROL/1'
            AND o.operation_id=NEW.operation_id AND o.actor_id=NEW.actor_id AND o.state='COMMITTED'
            AND o.xmin=pg_current_xact_id()::text::xid AND o.object_ids=ARRAY[NEW.location_id,NEW.id]
            AND convert_from(o.result_bytes,'UTF8')::jsonb @> jsonb_build_object('LocationId',NEW.location_id::text,
              'BlockId',NEW.block_id::text,'Effect',NEW.kind,'Reference',jsonb_build_object('Owner',o.owner,'Id',NEW.id::text,'Version','1','Digest',NEW.digest)))
            THEN RAISE EXCEPTION 'LOCATION_CONTROL_OPERATION_REQUIRED' USING ERRCODE='23514';END IF;RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER control_event_original AFTER INSERT ON cp6_location_control_v1.events DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_location_control_v1.original();
        CREATE FUNCTION cp6_location_control_v1.native_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='UPDATE' THEN
            IF (NEW."Id",NEW."TenantId",NEW."WarehouseCd",NEW."LocationCd",NEW."IsDeleted") IS DISTINCT FROM (OLD."Id",OLD."TenantId",OLD."WarehouseCd",OLD."LocationCd",OLD."IsDeleted")
              AND EXISTS(SELECT FROM cp6_location_control_v1.controls WHERE location_id=OLD."Id") THEN RAISE EXCEPTION 'LOCATION_CONTROL_IDENTITY_RESERVED' USING ERRCODE='23514';END IF;
            IF NOT NEW."IsBlocked" AND cp6_location_control_v1.open_blocks(OLD."TenantId",OLD."Id") THEN RAISE EXCEPTION 'LOCATION_LOCAL_BLOCK_ACTIVE' USING ERRCODE='23514';END IF;
            -- An independent writer setting the same true value may represent another restriction.
            -- Keep that contribution separate; this Owner never clears it on local release.
            IF pg_trigger_depth()=1 THEN
              UPDATE cp6_location_control_v1.controls SET other_blocked=NEW."IsBlocked",revision=revision+1
                WHERE tenant_id=OLD."TenantId" AND location_id=OLD."Id" AND other_blocked IS DISTINCT FROM NEW."IsBlocked";
            END IF;
            RETURN NEW;
          END IF;
          IF EXISTS(SELECT FROM cp6_location_control_v1.controls WHERE location_id=OLD."Id") THEN RAISE EXCEPTION 'LOCATION_CONTROL_IDENTITY_RESERVED' USING ERRCODE='23514';END IF;RETURN OLD;
        END $$;
        CREATE TRIGGER control_native_guard BEFORE UPDATE OF "IsBlocked","Id","TenantId","WarehouseCd","LocationCd","IsDeleted" OR DELETE ON public."T_Location" FOR EACH ROW EXECUTE FUNCTION cp6_location_control_v1.native_guard();
        CREATE FUNCTION cp6_location_control_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT NOT EXISTS(SELECT FROM (VALUES
            ('cp6_location_control_v1.controls','control_head_guard'),('cp6_location_control_v1.controls','control_head_truncate'),('cp6_location_control_v1.controls','control_draft_revision'),('cp6_location_control_v1.controls','control_head_original'),
            ('cp6_location_control_v1.events','control_events_immutable'),('cp6_location_control_v1.events','control_event_apply'),
            ('cp6_location_control_v1.events','control_event_original'),('public."T_Location"','control_native_guard')) x(tbl,name)
            WHERE NOT EXISTS(SELECT FROM pg_trigger g WHERE g.tgrelid=to_regclass(x.tbl) AND g.tgname=x.name AND g.tgenabled IN('O','A')))
        $$;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_location_control_v1 FROM PUBLIC;
        -- Register actions, never grant an existing role new authority.
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
          SELECT gen_random_uuid(),m."MenuId",a.code,a.name,90,t."Id",LOCALTIMESTAMP FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
          CROSS JOIN (VALUES('block','加本域封锁'),('unblock','解除指定本域封锁')) a(code,name)
          WHERE m."MenuKey"='wms-location' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Local control decisions require forward recovery.");
}
