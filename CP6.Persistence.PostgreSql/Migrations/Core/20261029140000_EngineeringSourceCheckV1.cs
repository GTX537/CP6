using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261029140000_EngineeringSourceCheckV1")]
public sealed class EngineeringSourceCheckV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE OR REPLACE FUNCTION cp6_configuration_rule_v1.validate_original() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE body jsonb=convert_from(NEW.body_bytes,'UTF8')::jsonb; previous jsonb; active bigint; expected_active bigint; old_digest text;
        BEGIN
          SELECT active_version INTO active FROM cp6_configuration_rule_v1.heads WHERE tenant_id=NEW.tenant_id AND rule_id=NEW.rule_id;
          IF body->>'tenantId' IS DISTINCT FROM NEW.tenant_id::text OR body->>'id' IS DISTINCT FROM NEW.rule_id::text
             OR body->>'version' IS DISTINCT FROM NEW.revision::text OR body->>'actorId' IS DISTINCT FROM NEW.actor_id::text
             OR body->>'etag' IS NULL OR body->>'etag'='00000000-0000-0000-0000-000000000000'
             OR coalesce(body#>>'{body,scope}','') NOT IN('ENGINEERING_OPTIONS_ONLY','ENGINEERING_OPTIONS_AND_CHECKS_V1')
             OR (body#>>'{body,scope}'='ENGINEERING_OPTIONS_ONLY' AND coalesce(body#>'{body,engineeringChecks}','null'::jsonb)<>'null'::jsonb)
             OR (body#>>'{body,scope}'='ENGINEERING_OPTIONS_AND_CHECKS_V1' AND body#>>'{body,engineeringChecks,schema}' IS DISTINCT FROM 'engineering-check-policy/1')
             OR NOT EXISTS(SELECT FROM cp6_configuration_rule_v1.heads h WHERE h.tenant_id=NEW.tenant_id AND h.rule_id=NEW.rule_id
               AND h.revision=NEW.revision AND h.creator_id::text=body->>'creatorId' AND h.product_id::text=body#>>'{body,productRef,id}') THEN
            RAISE EXCEPTION 'CONFIGURATION_RULE_IDENTITY_INVALID' USING ERRCODE='23514';
          END IF;
          IF NEW.revision=1 THEN
            IF body->>'parentVersion' IS NOT NULL OR body->>'action' IS DISTINCT FROM 'CREATE' OR body->>'state' IS DISTINCT FROM 'DRAFT'
               OR body->>'previousActiveRef' IS NOT NULL THEN RAISE EXCEPTION 'CONFIGURATION_RULE_PARENT_INVALID' USING ERRCODE='23514'; END IF;
          ELSE
            SELECT convert_from(body_bytes,'UTF8')::jsonb INTO previous FROM cp6_configuration_rule_v1.versions
              WHERE tenant_id=NEW.tenant_id AND rule_id=NEW.rule_id AND revision=NEW.revision-1;
            IF previous IS NULL OR body->>'parentVersion' IS DISTINCT FROM (NEW.revision-1)::text OR body->>'etag'=previous->>'etag'
              OR body->>'action' NOT IN ('REVISE','SUBMIT','APPROVE','RETURN','WITHDRAW') THEN
              RAISE EXCEPTION 'CONFIGURATION_RULE_PARENT_INVALID' USING ERRCODE='23514'; END IF;
            expected_active=CASE WHEN previous->>'action'='APPROVE' THEN NEW.revision-1 WHEN previous->>'action'='WITHDRAW' THEN NULL
              ELSE (previous#>>'{previousActiveRef,version}')::bigint END;
            IF (body#>>'{previousActiveRef,version}')::bigint IS DISTINCT FROM expected_active THEN
              RAISE EXCEPTION 'CONFIGURATION_RULE_ACTIVE_INVALID' USING ERRCODE='23514'; END IF;
            IF expected_active IS NOT NULL THEN
              SELECT body_digest INTO old_digest FROM cp6_configuration_rule_v1.versions WHERE tenant_id=NEW.tenant_id AND rule_id=NEW.rule_id AND revision=expected_active;
              IF body#>>'{previousActiveRef,owner}' IS DISTINCT FROM 'Main.Plm.ConfigurationRule.v1' OR body#>>'{previousActiveRef,id}' IS DISTINCT FROM NEW.rule_id::text
                OR body#>>'{previousActiveRef,digest}' IS DISTINCT FROM old_digest THEN RAISE EXCEPTION 'CONFIGURATION_RULE_ACTIVE_INVALID' USING ERRCODE='23514'; END IF;
            END IF;
            IF body->>'action' <> 'REVISE' AND body->'body' IS DISTINCT FROM previous->'body' THEN
              RAISE EXCEPTION 'CONFIGURATION_RULE_CONTENT_INVALID' USING ERRCODE='23514'; END IF;
            IF NOT (CASE body->>'action'
              WHEN 'REVISE' THEN previous->>'state'<>'UNDER_REVIEW' AND body->>'state'='DRAFT'
              WHEN 'SUBMIT' THEN previous->>'state'='DRAFT' AND body->>'state'='UNDER_REVIEW'
              WHEN 'APPROVE' THEN previous->>'state'='UNDER_REVIEW' AND body->>'state'='APPROVED'
              WHEN 'RETURN' THEN previous->>'state'='UNDER_REVIEW' AND body->>'state'='DRAFT'
              WHEN 'WITHDRAW' THEN expected_active IS NOT NULL AND body->>'state'=CASE WHEN previous->>'state'='APPROVED' THEN 'WITHDRAWN' ELSE previous->>'state' END
              ELSE FALSE END) THEN RAISE EXCEPTION 'CONFIGURATION_RULE_STATE_INVALID' USING ERRCODE='23514'; END IF;
          END IF;
          expected_active=CASE WHEN body->>'action'='APPROVE' THEN NEW.revision WHEN body->>'action'='WITHDRAW' THEN NULL ELSE expected_active END;
          IF active IS DISTINCT FROM expected_active THEN RAISE EXCEPTION 'CONFIGURATION_RULE_ACTIVE_INVALID' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE SCHEMA cp6_engineering_check_v1;
        REVOKE ALL ON SCHEMA cp6_engineering_check_v1 FROM PUBLIC;
        CREATE TABLE cp6_engineering_check_v1.runs(
          tenant_id uuid NOT NULL,id uuid NOT NULL,revision_id uuid NOT NULL,operation_id uuid NOT NULL,actor_id uuid NOT NULL,
          body bytea NOT NULL CHECK(octet_length(body) BETWEEN 2 AND 134217728),digest text NOT NULL CHECK(digest=encode(sha256(body),'hex')),
          recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          journal text NOT NULL DEFAULT 'Main.Plm.EngineeringSourceCheck.v1' CHECK(journal='Main.Plm.EngineeringSourceCheck.v1'),
          PRIMARY KEY(tenant_id,id),UNIQUE(tenant_id,operation_id),
          FOREIGN KEY(tenant_id,revision_id) REFERENCES cp6_engineering_revision_v1.heads(tenant_id,revision_id),
          FOREIGN KEY(tenant_id,journal,operation_id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED);
        CREATE INDEX engineering_check_history ON cp6_engineering_check_v1.runs(tenant_id,revision_id,recorded_at DESC,id);
        CREATE FUNCTION cp6_engineering_check_v1.immutable() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN RAISE EXCEPTION 'ENGINEERING_CHECK_IMMUTABLE'; END $$;
        CREATE TRIGGER engineering_check_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_engineering_check_v1.runs
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_engineering_check_v1.immutable();
        CREATE FUNCTION cp6_engineering_check_v1.validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE p jsonb; BEGIN p=convert_from(NEW.body,'UTF8')::jsonb;
          IF p->>'tenantId' IS DISTINCT FROM NEW.tenant_id::text OR p->>'id' IS DISTINCT FROM NEW.id::text
            OR p->>'actorId' IS DISTINCT FROM NEW.actor_id::text OR p#>>'{command,revision,id}' IS DISTINCT FROM NEW.revision_id::text
            OR p#>>'{command,operationId}' IS DISTINCT FROM NEW.operation_id::text OR p->>'purpose' IS DISTINCT FROM 'TECHNICAL_PRECHECK_ONLY'
            OR coalesce(p->>'state','') NOT IN('PASS','BLOCK','UNKNOWN') THEN RAISE EXCEPTION 'ENGINEERING_CHECK_INVALID'; END IF;
          IF NOT EXISTS(SELECT FROM cp6_main_owner_v1.operations o WHERE o.tenant_id=NEW.tenant_id AND o.owner=NEW.journal
            AND o.operation_id=NEW.operation_id AND o.actor_id=NEW.actor_id AND o.contract='Main.Plm.EngineeringSourceCheck.commands.v1'
            AND o.state='COMMITTED' AND o.object_ids=ARRAY[NEW.revision_id]) THEN RAISE EXCEPTION 'ENGINEERING_CHECK_ORIGINAL_REQUIRED'; END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER engineering_check_provenance AFTER INSERT ON cp6_engineering_check_v1.runs
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_engineering_check_v1.validate();
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_engineering_check_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_engineering_check_v1 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Engineering check evidence is forward-only.");
}
