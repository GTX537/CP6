using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)), Migration("20261028010000_ToolInstanceAssociationV1")]
public sealed class ToolInstanceAssociationV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_tool_association_v1;
        REVOKE ALL ON SCHEMA cp6_tool_association_v1 FROM PUBLIC;
        CREATE TABLE cp6_tool_association_v1.proposals (
          tenant_id uuid NOT NULL,proposal_id uuid NOT NULL,physical_id uuid NOT NULL,technical_id uuid NOT NULL,
          body_bytes bytea NOT NULL CHECK(octet_length(body_bytes) BETWEEN 2 AND 65536),
          body_digest text COLLATE "C" NOT NULL CHECK(body_digest=encode(sha256(body_bytes),'hex')),
          PRIMARY KEY(tenant_id,proposal_id),UNIQUE(tenant_id,proposal_id,physical_id),UNIQUE(tenant_id,proposal_id,body_digest),
          FOREIGN KEY(tenant_id,physical_id) REFERENCES cp6_tool_instance_v1.heads(tenant_id,object_id),
          FOREIGN KEY(tenant_id,technical_id) REFERENCES cp6_tool_specification_v1.heads(tenant_id,tool_id));
        CREATE TABLE cp6_tool_association_v1.heads (
          tenant_id uuid NOT NULL,proposal_id uuid NOT NULL,physical_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          state text NOT NULL CHECK(state IN ('PROPOSED','UNKNOWN','CONFIRMED','REJECTED','REVOKED')),
          PRIMARY KEY(tenant_id,proposal_id),FOREIGN KEY(tenant_id,proposal_id,physical_id) REFERENCES cp6_tool_association_v1.proposals(tenant_id,proposal_id,physical_id));
        CREATE UNIQUE INDEX one_confirmed_tool_association ON cp6_tool_association_v1.heads(tenant_id,physical_id) WHERE state='CONFIRMED';
        CREATE TABLE cp6_tool_association_v1.versions (
          tenant_id uuid NOT NULL,proposal_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          body_bytes bytea NOT NULL CHECK(octet_length(body_bytes) BETWEEN 2 AND 16384),
          body_digest text COLLATE "C" NOT NULL CHECK(body_digest=encode(sha256(body_bytes),'hex')),
          PRIMARY KEY(tenant_id,proposal_id,revision),
          FOREIGN KEY(tenant_id,proposal_id) REFERENCES cp6_tool_association_v1.heads DEFERRABLE INITIALLY DEFERRED);
        ALTER TABLE cp6_tool_association_v1.heads ADD CONSTRAINT tool_association_original
          FOREIGN KEY(tenant_id,proposal_id,revision) REFERENCES cp6_tool_association_v1.versions DEFERRABLE INITIALLY DEFERRED;
        CREATE TABLE cp6_tool_association_v1.decisions (
          tenant_id uuid NOT NULL,proposal_id uuid NOT NULL,receipt_id uuid NOT NULL,role text NOT NULL,owner text NOT NULL,
          round_id uuid NOT NULL,subject_digest text COLLATE "C" NOT NULL,
          body_bytes bytea NOT NULL CHECK(octet_length(body_bytes) BETWEEN 2 AND 16384),
          body_digest text COLLATE "C" NOT NULL CHECK(body_digest=encode(sha256(body_bytes),'hex')),
          PRIMARY KEY(tenant_id,receipt_id),FOREIGN KEY(tenant_id,proposal_id,subject_digest) REFERENCES cp6_tool_association_v1.proposals(tenant_id,proposal_id,body_digest),
          CHECK((role='ENG_SPEC_CONFIRMATION' AND owner='Main.Plm.ToolInstanceConformance.v1')
             OR (role='PHYSICAL_INSTANCE_ASSOCIATION' AND owner='Main.Wms.ToolAssociationAdoption.v1')));
        CREATE FUNCTION cp6_tool_association_v1.immutable() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN RAISE EXCEPTION 'TOOL_ASSOCIATION_ORIGINAL_IMMUTABLE' USING ERRCODE='23514'; END $$;
        CREATE FUNCTION cp6_tool_association_v1.advance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.revision<>1 THEN RAISE EXCEPTION 'TOOL_ASSOCIATION_FRONTIER_INVALID' USING ERRCODE='23514'; END IF;
          ELSIF (NEW.tenant_id,NEW.proposal_id,NEW.physical_id) IS DISTINCT FROM (OLD.tenant_id,OLD.proposal_id,OLD.physical_id)
            OR NEW.revision<>OLD.revision+1 OR OLD.state IN ('REJECTED','REVOKED') THEN
            RAISE EXCEPTION 'TOOL_ASSOCIATION_FRONTIER_INVALID' USING ERRCODE='23514';
          END IF; RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_tool_association_v1.validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE b jsonb=convert_from(NEW.body_bytes,'UTF8')::jsonb; p cp6_tool_association_v1.proposals%ROWTYPE; h cp6_tool_association_v1.heads%ROWTYPE;
        BEGIN
          IF b->>'tenantId' IS DISTINCT FROM NEW.tenant_id::text OR b->>'proposalId' IS DISTINCT FROM NEW.proposal_id::text THEN
            RAISE EXCEPTION 'TOOL_ASSOCIATION_ORIGINAL_INVALID' USING ERRCODE='23514'; END IF;
          IF TG_TABLE_NAME='proposals' THEN
            IF b->>'proposalRevision' IS DISTINCT FROM '1' OR b#>>'{technical,owner}' IS DISTINCT FROM 'Main.Plm.ToolSpecification.v1'
              OR b#>>'{physical,owner}' IS DISTINCT FROM 'Main.Wms.ToolInstance.v1'
              OR b#>>'{physical,id}' IS DISTINCT FROM NEW.physical_id::text OR b#>>'{technical,id}' IS DISTINCT FROM NEW.technical_id::text
              OR NOT EXISTS(SELECT FROM cp6_tool_specification_v1.versions s WHERE s.tenant_id=NEW.tenant_id AND s.tool_id=NEW.technical_id
                AND s.revision::text=b#>>'{technical,version}' AND s.body_digest=b#>>'{technical,digest}')
              OR NOT EXISTS(SELECT FROM cp6_tool_instance_v1.versions s WHERE s.tenant_id=NEW.tenant_id AND s.object_id=NEW.physical_id
                AND s.revision::text=b#>>'{physical,version}' AND s.source_digest=b#>>'{physical,digest}') THEN
              RAISE EXCEPTION 'TOOL_ASSOCIATION_ORIGINAL_INVALID' USING ERRCODE='23514'; END IF;
          ELSE
            SELECT * INTO STRICT p FROM cp6_tool_association_v1.proposals WHERE tenant_id=NEW.tenant_id AND proposal_id=NEW.proposal_id;
            IF b->>'subjectDigest' IS DISTINCT FROM p.body_digest THEN RAISE EXCEPTION 'TOOL_ASSOCIATION_SUBJECT_INVALID' USING ERRCODE='23514'; END IF;
            IF TG_TABLE_NAME='versions' THEN
              SELECT * INTO STRICT h FROM cp6_tool_association_v1.heads WHERE tenant_id=NEW.tenant_id AND proposal_id=NEW.proposal_id;
              IF b->>'revision' IS DISTINCT FROM NEW.revision::text OR h.revision<>NEW.revision OR h.state IS DISTINCT FROM b->>'state'
                OR NOT (b->>'controlRevision' ~ '^[1-9][0-9]*$' AND b->>'generation' ~ '^[1-9][0-9]*$')
                OR NEW.revision>1 AND NOT EXISTS(SELECT FROM cp6_tool_association_v1.versions v WHERE v.tenant_id=NEW.tenant_id AND v.proposal_id=NEW.proposal_id AND v.revision=NEW.revision-1) THEN
                RAISE EXCEPTION 'TOOL_ASSOCIATION_PROJECTION_INVALID' USING ERRCODE='23514'; END IF;
            ELSIF b->>'id' IS DISTINCT FROM NEW.receipt_id::text OR b->>'role' IS DISTINCT FROM NEW.role OR b->>'owner' IS DISTINCT FROM NEW.owner
              OR b->>'roundId' IS DISTINCT FROM NEW.round_id::text OR b->>'decision' NOT IN ('CONFIRM','REJECT','WITHDRAW') THEN
              RAISE EXCEPTION 'TOOL_ASSOCIATION_DECISION_INVALID' USING ERRCODE='23514';
            END IF;
          END IF; RETURN NEW;
        END $$;
        CREATE TRIGGER tool_association_proposal_validate BEFORE INSERT ON cp6_tool_association_v1.proposals FOR EACH ROW EXECUTE FUNCTION cp6_tool_association_v1.validate();
        CREATE TRIGGER tool_association_version_validate BEFORE INSERT ON cp6_tool_association_v1.versions FOR EACH ROW EXECUTE FUNCTION cp6_tool_association_v1.validate();
        CREATE TRIGGER tool_association_decision_validate BEFORE INSERT ON cp6_tool_association_v1.decisions FOR EACH ROW EXECUTE FUNCTION cp6_tool_association_v1.validate();
        CREATE TRIGGER tool_association_advance BEFORE INSERT OR UPDATE ON cp6_tool_association_v1.heads FOR EACH ROW EXECUTE FUNCTION cp6_tool_association_v1.advance();
        CREATE TRIGGER tool_association_proposal_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_tool_association_v1.proposals FOR EACH STATEMENT EXECUTE FUNCTION cp6_tool_association_v1.immutable();
        CREATE TRIGGER tool_association_version_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_tool_association_v1.versions FOR EACH STATEMENT EXECUTE FUNCTION cp6_tool_association_v1.immutable();
        CREATE TRIGGER tool_association_decision_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_tool_association_v1.decisions FOR EACH STATEMENT EXECUTE FUNCTION cp6_tool_association_v1.immutable();
        CREATE TRIGGER tool_association_head_no_delete BEFORE DELETE OR TRUNCATE ON cp6_tool_association_v1.heads FOR EACH STATEMENT EXECUTE FUNCTION cp6_tool_association_v1.immutable();
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_tool_association_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_tool_association_v1 FROM PUBLIC;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,71,t."Id",LOCALTIMESTAMP FROM public."Sys_Menus" m
        CROSS JOIN public."Sys_Tenants" t CROSS JOIN (VALUES('plm-tool-specification','instance-associate','确认实例技术对应'),
          ('wms-plate-mold','engineering-link','确认实物工程关联')) a(menu,code,name)
        WHERE m."MenuKey"=a.menu AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Association originals require forward recovery.");
}
