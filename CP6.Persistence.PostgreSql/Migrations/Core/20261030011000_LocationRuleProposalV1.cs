using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261030011000_LocationRuleProposalV1")]
public sealed class LocationRuleProposalV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE SCHEMA cp6_location_rule_v1;
        REVOKE ALL ON SCHEMA cp6_location_rule_v1 FROM PUBLIC;
        CREATE TABLE cp6_location_rule_v1.selectors(
          tenant_id uuid NOT NULL,id uuid NOT NULL,location_id uuid NOT NULL REFERENCES public."T_Location"("Id"),
          family text COLLATE "C" NOT NULL CHECK(length(family) BETWEEN 1 AND 64),metric text COLLATE "C" NOT NULL CHECK(length(metric) BETWEEN 1 AND 100),
          domain text COLLATE "C" NOT NULL CHECK(length(domain) BETWEEN 1 AND 100),revision bigint NOT NULL CHECK(revision=0),current_rule_version_id uuid CHECK(current_rule_version_id IS NULL),
          operation_id uuid NOT NULL,actor_id uuid NOT NULL,PRIMARY KEY(tenant_id,id),UNIQUE(tenant_id,id,location_id),UNIQUE(tenant_id,location_id,family,metric,domain));
        CREATE TABLE cp6_location_rule_v1.proposals(
          tenant_id uuid NOT NULL,id uuid NOT NULL,location_id uuid NOT NULL,selector_id uuid NOT NULL,
          version bigint NOT NULL CHECK(version>0),revision bigint NOT NULL CHECK(revision>0),token uuid NOT NULL,
          state text NOT NULL CHECK(state IN('DRAFT','WITHDRAWN')),operation_id uuid NOT NULL,actor_id uuid NOT NULL,PRIMARY KEY(tenant_id,id),
          FOREIGN KEY(tenant_id,selector_id,location_id) REFERENCES cp6_location_rule_v1.selectors(tenant_id,id,location_id));
        CREATE TABLE cp6_location_rule_v1.versions(
          tenant_id uuid NOT NULL,id uuid NOT NULL,version bigint NOT NULL CHECK(version>0),operation_id uuid NOT NULL,actor_id uuid NOT NULL,
          body bytea NOT NULL,digest text NOT NULL CHECK(digest=encode(sha256(body),'hex')),PRIMARY KEY(tenant_id,id,version),UNIQUE(tenant_id,operation_id),
          FOREIGN KEY(tenant_id,id) REFERENCES cp6_location_rule_v1.proposals);
        ALTER TABLE cp6_location_rule_v1.proposals ADD CONSTRAINT rule_current_original FOREIGN KEY(tenant_id,id,version)
          REFERENCES cp6_location_rule_v1.versions DEFERRABLE INITIALLY DEFERRED;
        CREATE FUNCTION cp6_location_rule_v1.selector_insert() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF NOT EXISTS(SELECT FROM public."T_Location" l WHERE l."TenantId"=NEW.tenant_id AND l."Id"=NEW.location_id AND NOT l."IsDeleted")
            THEN RAISE EXCEPTION 'RULE_NATIVE_IDENTITY_REQUIRED';END IF;RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_location_rule_v1.advance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='INSERT' THEN IF NEW.version<>1 OR NEW.revision<>1 OR NEW.state<>'DRAFT' THEN RAISE EXCEPTION 'RULE_FRONTIER_INVALID';END IF;
          ELSIF (to_jsonb(NEW)-ARRAY['version','revision','token','state','operation_id','actor_id']) IS DISTINCT FROM
              (to_jsonb(OLD)-ARRAY['version','revision','token','state','operation_id','actor_id']) OR OLD.state<>'DRAFT'
            OR NEW.revision<>OLD.revision+1 OR NEW.token=OLD.token OR NEW.operation_id=OLD.operation_id
            OR NOT (NEW.state='DRAFT' AND NEW.version=OLD.version+1 OR NEW.state='WITHDRAWN' AND NEW.version=OLD.version)
            THEN RAISE EXCEPTION 'RULE_FRONTIER_INVALID';END IF;RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_location_rule_v1.validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE b jsonb=convert_from(NEW.body,'UTF8')::jsonb;d jsonb=b->'draft';c jsonb=d->'capacity';
          p cp6_location_rule_v1.proposals%ROWTYPE;s cp6_location_rule_v1.selectors%ROWTYPE;BEGIN
          SELECT * INTO STRICT p FROM cp6_location_rule_v1.proposals WHERE tenant_id=NEW.tenant_id AND id=NEW.id;
          SELECT * INTO STRICT s FROM cp6_location_rule_v1.selectors WHERE tenant_id=NEW.tenant_id AND id=p.selector_id;
          IF b->>'tenantId' IS DISTINCT FROM NEW.tenant_id::text OR b->>'id' IS DISTINCT FROM NEW.id::text
            OR b->>'locationId' IS DISTINCT FROM p.location_id::text OR b->>'selectorId' IS DISTINCT FROM p.selector_id::text
            OR b->>'version' IS DISTINCT FROM NEW.version::text OR b->>'operationId' IS DISTINCT FROM NEW.operation_id::text
            OR b->>'actorId' IS DISTINCT FROM NEW.actor_id::text OR b->>'referenceResolution' IS DISTINCT FROM 'UNRESOLVED'
            OR NEW.version<>p.version OR p.state<>'DRAFT' OR NEW.operation_id<>p.operation_id OR NEW.actor_id<>p.actor_id
            OR d->'selectorKey'->>'ruleFamily' IS DISTINCT FROM s.family OR d->'selectorKey'->>'metricId' IS DISTINCT FROM s.metric
            OR d->'selectorKey'->'applicabilityDomainRef'->>'id' IS DISTINCT FROM s.domain
            OR jsonb_typeof(d->'allowedProductTypes') IS DISTINCT FROM 'array' OR jsonb_typeof(d->'allowedOperations') IS DISTINCT FROM 'array'
            OR jsonb_typeof(d->'isPickable') IS DISTINCT FROM 'boolean' OR NOT(d ? 'validFrom' AND d ? 'validTo')
            OR c->>'mode' IS NULL OR c->>'mode' NOT IN('LIMITED','UNLIMITED','UNKNOWN')
            OR c->>'mode'='LIMITED' AND (COALESCE(c->>'value','') !~ '^[0-9]{1,13}\.[0-9]{8}$' OR c->'metricRef'->>'id' IS DISTINCT FROM s.metric
                OR jsonb_typeof(c->'uomRef') IS DISTINCT FROM 'object' OR jsonb_typeof(c->'precisionPolicyRef') IS DISTINCT FROM 'object' OR c ? 'reason')
            OR c->>'mode'='UNLIMITED' AND c IS DISTINCT FROM jsonb_build_object('mode','UNLIMITED')
            OR c->>'mode'='UNKNOWN' AND (jsonb_typeof(c->'reason') IS DISTINCT FROM 'string' OR c ?| ARRAY['value','metricRef','uomRef','precisionPolicyRef'])
            OR NEW.version>1 AND NOT EXISTS(SELECT FROM cp6_location_rule_v1.versions WHERE tenant_id=NEW.tenant_id AND id=NEW.id AND version=NEW.version-1)
            THEN RAISE EXCEPTION 'RULE_ORIGINAL_INVALID';END IF;RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_location_rule_v1.provenance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE p cp6_location_rule_v1.proposals%ROWTYPE;BEGIN
          SELECT * INTO STRICT p FROM cp6_location_rule_v1.proposals WHERE tenant_id=NEW.tenant_id AND id=NEW.id;
          IF NOT EXISTS(SELECT FROM cp6_main_owner_v1.operations o JOIN cp6_location_rule_v1.versions v
            ON v.tenant_id=o.tenant_id AND v.id=p.id AND v.version=p.version
            WHERE o.tenant_id=NEW.tenant_id AND o.owner='Main.Wms.LocationRuleProposal.v1' AND o.contract='LOCATION-RULE-PROPOSAL/1'
            AND o.operation_id=NEW.operation_id AND o.actor_id=NEW.actor_id AND o.state='COMMITTED'
            AND o.xmin=pg_current_xact_id()::text::xid AND o.object_ids=ARRAY[p.location_id,p.id]
            AND convert_from(o.result_bytes,'UTF8')::jsonb @> jsonb_build_object('LocationId',p.location_id::text,'SelectorId',p.selector_id::text,
              'State',p.state,'Etag','"'||replace(p.token::text,'-','')||'"',
              'Reference',jsonb_build_object('Owner',o.owner,'Id',p.id::text,'Version',p.version::text,'Digest',v.digest)))
            THEN RAISE EXCEPTION 'RULE_OPERATION_REQUIRED';END IF;RETURN NULL;
        END $$;
        CREATE FUNCTION cp6_location_rule_v1.selector_provenance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF NOT EXISTS(SELECT FROM cp6_location_rule_v1.proposals p JOIN cp6_main_owner_v1.operations o
            ON o.tenant_id=p.tenant_id AND o.operation_id=NEW.operation_id AND o.actor_id=NEW.actor_id
            WHERE p.tenant_id=NEW.tenant_id AND p.selector_id=NEW.id AND p.operation_id=NEW.operation_id
            AND o.owner='Main.Wms.LocationRuleProposal.v1' AND o.state='COMMITTED' AND o.xmin=pg_current_xact_id()::text::xid)
            THEN RAISE EXCEPTION 'RULE_SELECTOR_OPERATION_REQUIRED';END IF;RETURN NULL;
        END $$;
        CREATE FUNCTION cp6_location_rule_v1.native_identity() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF (NEW."Id",NEW."TenantId") IS DISTINCT FROM (OLD."Id",OLD."TenantId") AND
            EXISTS(SELECT FROM cp6_location_rule_v1.selectors WHERE location_id=OLD."Id") THEN RAISE EXCEPTION 'RULE_IDENTITY_RESERVED';END IF;RETURN NEW;
        END $$;
        CREATE TRIGGER rule_selector_insert BEFORE INSERT ON cp6_location_rule_v1.selectors FOR EACH ROW EXECUTE FUNCTION cp6_location_rule_v1.selector_insert();
        CREATE TRIGGER rule_selector_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_location_rule_v1.selectors FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE CONSTRAINT TRIGGER rule_selector_provenance AFTER INSERT ON cp6_location_rule_v1.selectors DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_location_rule_v1.selector_provenance();
        CREATE TRIGGER rule_advance BEFORE INSERT OR UPDATE ON cp6_location_rule_v1.proposals FOR EACH ROW EXECUTE FUNCTION cp6_location_rule_v1.advance();
        CREATE TRIGGER rule_proposals_permanent BEFORE DELETE OR TRUNCATE ON cp6_location_rule_v1.proposals FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE CONSTRAINT TRIGGER rule_provenance AFTER INSERT OR UPDATE ON cp6_location_rule_v1.proposals DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_location_rule_v1.provenance();
        CREATE TRIGGER rule_validate BEFORE INSERT ON cp6_location_rule_v1.versions FOR EACH ROW EXECUTE FUNCTION cp6_location_rule_v1.validate();
        CREATE TRIGGER rule_versions_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_location_rule_v1.versions FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER rule_native_identity BEFORE UPDATE ON public."T_Location" FOR EACH ROW EXECUTE FUNCTION cp6_location_rule_v1.native_identity();
        CREATE FUNCTION cp6_location_rule_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT NOT EXISTS(SELECT FROM (VALUES
            ('cp6_location_rule_v1.selectors','rule_selector_insert','cp6_location_rule_v1.selector_insert()',7,false),
            ('cp6_location_rule_v1.selectors','rule_selector_immutable','cp6_scope_authorization_v1.immutable()',58,false),
            ('cp6_location_rule_v1.selectors','rule_selector_provenance','cp6_location_rule_v1.selector_provenance()',5,true),
            ('cp6_location_rule_v1.proposals','rule_advance','cp6_location_rule_v1.advance()',23,false),
            ('cp6_location_rule_v1.proposals','rule_proposals_permanent','cp6_scope_authorization_v1.immutable()',42,false),
            ('cp6_location_rule_v1.proposals','rule_provenance','cp6_location_rule_v1.provenance()',21,true),
            ('cp6_location_rule_v1.versions','rule_validate','cp6_location_rule_v1.validate()',7,false),
            ('cp6_location_rule_v1.versions','rule_versions_immutable','cp6_scope_authorization_v1.immutable()',58,false),
            ('public."T_Location"','rule_native_identity','cp6_location_rule_v1.native_identity()',19,false)
          ) a(tbl,name,fn,kind,deferred) WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass(a.tbl)
            AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.kind AND t.tgenabled IN('O','A')
            AND t.tgqual IS NULL AND t.tgnargs=0 AND (NOT a.deferred OR t.tgdeferrable AND t.tginitdeferred)));
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_location_rule_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_location_rule_v1 FROM PUBLIC;
        LOCK TABLE public."Sys_Menus" IN SHARE ROW EXCLUSIVE MODE;
        DO $$ DECLARE id integer;BEGIN
          IF NOT EXISTS(SELECT FROM public."Sys_Menus" WHERE "MenuKey"='wms-location-rule') THEN
            SELECT CASE WHEN COALESCE(MAX("MenuId"),0)<2147483647 THEN COALESCE(MAX("MenuId"),0)+1 ELSE
              (SELECT min(candidate) FROM (SELECT 1 AS candidate UNION SELECT "MenuId"+1 FROM public."Sys_Menus" WHERE "MenuId">=1 AND "MenuId"<2147483647) a
               WHERE NOT EXISTS(SELECT FROM public."Sys_Menus" x WHERE x."MenuId"=a.candidate)) END INTO id FROM public."Sys_Menus";
            INSERT INTO public."Sys_Menus"("MenuId","MenuKey","MenuName","RoutePath","Icon","ParentId","OrderNo","Enable","CreateDate")
            VALUES(id,'wms-location-rule','库位规则候选',NULL,'Setting',NULL,161,true,LOCALTIMESTAMP);
          END IF;
        END $$;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.code,a.sort,t."Id",LOCALTIMESTAMP FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        CROSS JOIN (VALUES('query',0),('draft',1),('edit',2),('withdraw',3),('abandon',4)) a(code,sort)
        WHERE m."MenuKey"='wms-location-rule' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Rule proposal originals require forward recovery.");
}
