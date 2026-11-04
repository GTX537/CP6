using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261104100000_RequirementImpactV1")]
public sealed class RequirementImpactV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE SCHEMA cp6_requirement_impact_v1;
        CREATE TABLE cp6_requirement_impact_v1.requests(
          tenant_id uuid NOT NULL,id uuid NOT NULL,sequence bigint GENERATED ALWAYS AS IDENTITY,
          candidate_id uuid NOT NULL,candidate_revision bigint NOT NULL,
          action text NOT NULL CHECK(action IN('REQUEST','WITHDRAW')),previous_id uuid,
          actor_id uuid NOT NULL,reason text NOT NULL CHECK(length(reason)>0),recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          journal text NOT NULL DEFAULT 'Main.Plm.RequirementSet.v1' CHECK(journal='Main.Plm.RequirementSet.v1'),
          PRIMARY KEY(tenant_id,id),UNIQUE(sequence),
          FOREIGN KEY(tenant_id,candidate_id,candidate_revision) REFERENCES cp6_requirement_set_v1.versions,
          FOREIGN KEY(tenant_id,previous_id) REFERENCES cp6_requirement_impact_v1.requests,
          FOREIGN KEY(tenant_id,journal,id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED);
        CREATE INDEX requirement_impact_requests_source ON cp6_requirement_impact_v1.requests(tenant_id,candidate_id,candidate_revision,sequence DESC);
        CREATE TABLE cp6_requirement_impact_v1.cases(
          tenant_id uuid NOT NULL,id uuid NOT NULL,request_id uuid NOT NULL,candidate_id uuid NOT NULL,candidate_revision bigint NOT NULL,
          iteration_id uuid NOT NULL,iteration_revision bigint NOT NULL,input_version bigint NOT NULL CHECK(input_version>0),
          adopted_revision bigint NOT NULL,plan_version bigint NOT NULL CHECK(plan_version>0),stage text NOT NULL,
          actor_id uuid NOT NULL,recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),operation_id uuid NOT NULL,
          journal text NOT NULL CHECK(journal IN('Main.Plm.RequirementSet.v1','Main.Plm.EngineeringIteration.v1')),
          PRIMARY KEY(tenant_id,id),UNIQUE(tenant_id,candidate_id,candidate_revision,iteration_id,input_version),
          CHECK(candidate_revision>adopted_revision),
          FOREIGN KEY(tenant_id,request_id) REFERENCES cp6_requirement_impact_v1.requests,
          FOREIGN KEY(tenant_id,candidate_id,candidate_revision) REFERENCES cp6_requirement_set_v1.versions,
          FOREIGN KEY(tenant_id,candidate_id,adopted_revision) REFERENCES cp6_requirement_set_v1.versions,
          FOREIGN KEY(tenant_id,iteration_id,iteration_revision) REFERENCES cp6_engineering_iteration_v1.versions,
          FOREIGN KEY(tenant_id,journal,operation_id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED);
        CREATE INDEX requirement_impact_consumer ON cp6_requirement_impact_v1.cases(tenant_id,iteration_id,input_version);
        CREATE TABLE cp6_requirement_impact_v1.assessments(
          tenant_id uuid NOT NULL,id uuid NOT NULL,case_id uuid NOT NULL,sequence bigint GENERATED ALWAYS AS IDENTITY,previous_id uuid,
          iteration_id uuid NOT NULL,iteration_revision bigint NOT NULL,input_version bigint NOT NULL,plan_version bigint NOT NULL,
          decision text NOT NULL CHECK(decision IN('KEEP_CURRENT','READY_TO_ADOPT','BLOCKED')),actor_id uuid NOT NULL,
          reason text NOT NULL CHECK(length(reason)>0),recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          journal text NOT NULL DEFAULT 'Main.Plm.EngineeringIteration.v1' CHECK(journal='Main.Plm.EngineeringIteration.v1'),
          PRIMARY KEY(tenant_id,id),UNIQUE(sequence),
          FOREIGN KEY(tenant_id,case_id) REFERENCES cp6_requirement_impact_v1.cases,
          FOREIGN KEY(tenant_id,previous_id) REFERENCES cp6_requirement_impact_v1.assessments,
          FOREIGN KEY(tenant_id,iteration_id,iteration_revision) REFERENCES cp6_engineering_iteration_v1.versions,
          FOREIGN KEY(tenant_id,journal,id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED);
        CREATE INDEX requirement_impact_assessment_case ON cp6_requirement_impact_v1.assessments(tenant_id,case_id,sequence DESC);
        CREATE FUNCTION cp6_requirement_impact_v1.validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE last_id uuid;body jsonb;c cp6_requirement_impact_v1.cases;r cp6_requirement_impact_v1.requests;
        BEGIN
          IF TG_TABLE_NAME='requests' THEN
            SELECT id INTO last_id FROM cp6_requirement_impact_v1.requests WHERE tenant_id=NEW.tenant_id AND candidate_id=NEW.candidate_id
              AND candidate_revision=NEW.candidate_revision ORDER BY sequence DESC LIMIT 1;
            IF NEW.previous_id IS DISTINCT FROM last_id OR (NEW.action='WITHDRAW' AND last_id IS NULL) THEN RAISE EXCEPTION 'REQUIREMENT_IMPACT_REQUEST_FRONTIER_INVALID';END IF;
          ELSE
            SELECT convert_from(body_bytes,'UTF8')::jsonb INTO body FROM cp6_engineering_iteration_v1.versions
              WHERE tenant_id=NEW.tenant_id AND object_id=NEW.iteration_id AND revision=NEW.iteration_revision;
            IF body#>>'{body,iteration,inputVersion}' IS DISTINCT FROM NEW.input_version::text
              OR body#>>'{body,iteration,planVersion}' IS DISTINCT FROM NEW.plan_version::text THEN RAISE EXCEPTION 'REQUIREMENT_IMPACT_INPUT_INVALID';END IF;
            IF TG_TABLE_NAME='cases' THEN
              SELECT * INTO r FROM cp6_requirement_impact_v1.requests WHERE tenant_id=NEW.tenant_id AND id=NEW.request_id;
              IF r.action IS DISTINCT FROM 'REQUEST' OR r.candidate_id IS DISTINCT FROM NEW.candidate_id OR r.candidate_revision IS DISTINCT FROM NEW.candidate_revision
                OR body#>>'{body,iteration,input,proof,requirementSet,id}' IS DISTINCT FROM NEW.candidate_id::text
                OR body#>>'{body,iteration,input,proof,requirementSet,version}' IS DISTINCT FROM NEW.adopted_revision::text
                OR body#>>'{body,iteration,stage}' IS DISTINCT FROM NEW.stage
                OR (NEW.journal='Main.Plm.EngineeringIteration.v1' AND body->>'operationId' IS DISTINCT FROM NEW.operation_id::text)
                THEN RAISE EXCEPTION 'REQUIREMENT_IMPACT_ADOPTION_INVALID';END IF;
            ELSE
              SELECT * INTO c FROM cp6_requirement_impact_v1.cases WHERE tenant_id=NEW.tenant_id AND id=NEW.case_id;
              SELECT id INTO last_id FROM cp6_requirement_impact_v1.assessments WHERE tenant_id=NEW.tenant_id AND case_id=NEW.case_id ORDER BY sequence DESC LIMIT 1;
              IF c.iteration_id IS DISTINCT FROM NEW.iteration_id OR c.input_version IS DISTINCT FROM NEW.input_version OR NEW.previous_id IS DISTINCT FROM last_id
                THEN RAISE EXCEPTION 'REQUIREMENT_IMPACT_ASSESSMENT_FRONTIER_INVALID';END IF;
            END IF;
          END IF;RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_requirement_impact_v1.provenance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE o cp6_main_owner_v1.operations;operation uuid;result jsonb;
        BEGIN
          IF TG_TABLE_NAME='cases' THEN operation=NEW.operation_id;ELSE operation=NEW.id;END IF;
          SELECT * INTO o FROM cp6_main_owner_v1.operations WHERE tenant_id=NEW.tenant_id AND owner=NEW.journal AND operation_id=operation;
          result=convert_from(o.result_bytes,'UTF8')::jsonb;
          IF o.state IS DISTINCT FROM 'COMMITTED' OR o.actor_id IS DISTINCT FROM NEW.actor_id
            OR o.contract IS DISTINCT FROM (CASE WHEN NEW.journal='Main.Plm.RequirementSet.v1' THEN 'Main.Plm.RequirementSet.commands.v1' ELSE 'Main.Plm.EngineeringIteration.commands.v1' END)
            THEN RAISE EXCEPTION 'REQUIREMENT_IMPACT_ORIGINAL_REQUIRED';END IF;
          IF TG_TABLE_NAME='requests' THEN
            IF o.object_ids IS DISTINCT FROM ARRAY[NEW.candidate_id] OR result#>>'{Impact,RequestId}' IS DISTINCT FROM NEW.id::text THEN RAISE EXCEPTION 'REQUIREMENT_IMPACT_ORIGINAL_REQUIRED';END IF;
          ELSIF TG_TABLE_NAME='assessments' THEN
            IF o.object_ids IS DISTINCT FROM ARRAY[NEW.iteration_id] OR result->>'ImpactAssessmentId' IS DISTINCT FROM NEW.id::text THEN RAISE EXCEPTION 'REQUIREMENT_IMPACT_ORIGINAL_REQUIRED';END IF;
          ELSIF (NEW.journal='Main.Plm.RequirementSet.v1' AND (o.object_ids IS DISTINCT FROM ARRAY[NEW.candidate_id] OR ((result#>'{Impact,CreatedCases}') @> to_jsonb(ARRAY[NEW.id::text])) IS DISTINCT FROM true))
            OR (NEW.journal='Main.Plm.EngineeringIteration.v1' AND (o.object_ids IS DISTINCT FROM ARRAY[NEW.iteration_id] OR ((result->'ImpactCases') @> to_jsonb(ARRAY[NEW.id::text])) IS DISTINCT FROM true))
            THEN RAISE EXCEPTION 'REQUIREMENT_IMPACT_ORIGINAL_REQUIRED';END IF;
          RETURN NULL;
        END $$;
        CREATE FUNCTION cp6_requirement_impact_v1.complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE result jsonb=convert_from(NEW.result_bytes,'UTF8')::jsonb;entry text;
        BEGIN
          IF NEW.owner='Main.Plm.RequirementSet.v1' AND result->'Impact' IS NOT NULL AND result->'Impact'<>'null'::jsonb THEN
            IF NOT EXISTS(SELECT FROM cp6_requirement_impact_v1.requests r WHERE r.tenant_id=NEW.tenant_id AND r.id=NEW.operation_id
                AND r.id::text=result#>>'{Impact,RequestId}' AND r.action=result#>>'{Impact,Action}') THEN RAISE EXCEPTION 'REQUIREMENT_IMPACT_EFFECT_REQUIRED';END IF;
            FOR entry IN SELECT jsonb_array_elements_text(result#>'{Impact,CreatedCases}') LOOP
              IF NOT EXISTS(SELECT FROM cp6_requirement_impact_v1.cases c WHERE c.tenant_id=NEW.tenant_id AND c.id::text=entry AND c.operation_id=NEW.operation_id AND c.journal=NEW.owner)
                THEN RAISE EXCEPTION 'REQUIREMENT_IMPACT_EFFECT_REQUIRED';END IF;
            END LOOP;
          ELSIF NEW.owner='Main.Plm.EngineeringIteration.v1' THEN
            IF result->>'ImpactAssessmentId' IS NOT NULL AND NOT EXISTS(SELECT FROM cp6_requirement_impact_v1.assessments a WHERE a.tenant_id=NEW.tenant_id
                AND a.id=NEW.operation_id AND a.id::text=result->>'ImpactAssessmentId') THEN RAISE EXCEPTION 'REQUIREMENT_IMPACT_EFFECT_REQUIRED';END IF;
            IF result->'ImpactCases' IS NOT NULL AND result->'ImpactCases'<>'null'::jsonb THEN
              FOR entry IN SELECT jsonb_array_elements_text(result->'ImpactCases') LOOP
                IF NOT EXISTS(SELECT FROM cp6_requirement_impact_v1.cases c WHERE c.tenant_id=NEW.tenant_id AND c.id::text=entry AND c.operation_id=NEW.operation_id AND c.journal=NEW.owner)
                  THEN RAISE EXCEPTION 'REQUIREMENT_IMPACT_EFFECT_REQUIRED';END IF;
              END LOOP;
            END IF;
          END IF;RETURN NULL;
        END $$;
        CREATE TRIGGER requirement_impact_request_validate BEFORE INSERT ON cp6_requirement_impact_v1.requests FOR EACH ROW EXECUTE FUNCTION cp6_requirement_impact_v1.validate();
        CREATE TRIGGER requirement_impact_case_validate BEFORE INSERT ON cp6_requirement_impact_v1.cases FOR EACH ROW EXECUTE FUNCTION cp6_requirement_impact_v1.validate();
        CREATE TRIGGER requirement_impact_assessment_validate BEFORE INSERT ON cp6_requirement_impact_v1.assessments FOR EACH ROW EXECUTE FUNCTION cp6_requirement_impact_v1.validate();
        CREATE TRIGGER requirement_impact_request_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_requirement_impact_v1.requests FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER requirement_impact_case_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_requirement_impact_v1.cases FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER requirement_impact_assessment_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_requirement_impact_v1.assessments FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE CONSTRAINT TRIGGER requirement_impact_request_original AFTER INSERT ON cp6_requirement_impact_v1.requests DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_requirement_impact_v1.provenance();
        CREATE CONSTRAINT TRIGGER requirement_impact_case_original AFTER INSERT ON cp6_requirement_impact_v1.cases DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_requirement_impact_v1.provenance();
        CREATE CONSTRAINT TRIGGER requirement_impact_assessment_original AFTER INSERT ON cp6_requirement_impact_v1.assessments DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_requirement_impact_v1.provenance();
        CREATE CONSTRAINT TRIGGER requirement_impact_effect_complete AFTER INSERT ON cp6_main_owner_v1.operations DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_requirement_impact_v1.complete();
        CREATE FUNCTION cp6_requirement_impact_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT NOT EXISTS(SELECT FROM (VALUES
            ('cp6_requirement_impact_v1.requests','requirement_impact_request_validate','cp6_requirement_impact_v1.validate()',7),
            ('cp6_requirement_impact_v1.cases','requirement_impact_case_validate','cp6_requirement_impact_v1.validate()',7),
            ('cp6_requirement_impact_v1.assessments','requirement_impact_assessment_validate','cp6_requirement_impact_v1.validate()',7),
            ('cp6_requirement_impact_v1.requests','requirement_impact_request_immutable','cp6_scope_authorization_v1.immutable()',58),
            ('cp6_requirement_impact_v1.cases','requirement_impact_case_immutable','cp6_scope_authorization_v1.immutable()',58),
            ('cp6_requirement_impact_v1.assessments','requirement_impact_assessment_immutable','cp6_scope_authorization_v1.immutable()',58),
            ('cp6_requirement_impact_v1.requests','requirement_impact_request_original','cp6_requirement_impact_v1.provenance()',5),
            ('cp6_requirement_impact_v1.cases','requirement_impact_case_original','cp6_requirement_impact_v1.provenance()',5),
            ('cp6_requirement_impact_v1.assessments','requirement_impact_assessment_original','cp6_requirement_impact_v1.provenance()',5),
            ('cp6_main_owner_v1.operations','requirement_impact_effect_complete','cp6_requirement_impact_v1.complete()',5)) a(rel,name,fn,typ)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass(a.rel) AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn)
              AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL));
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_requirement_impact_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_requirement_impact_v1 FROM PUBLIC;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,11,t."Id",LOCALTIMESTAMP FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        JOIN(VALUES('plm-requirement-set','impact-request','需求后继影响请求'),('plm-engineering-iteration','impact-assess','迭代输入影响评估')) a(resource,code,name) ON true
        WHERE m."MenuKey"=a.resource AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Requirement impact originals require forward recovery.");
}
