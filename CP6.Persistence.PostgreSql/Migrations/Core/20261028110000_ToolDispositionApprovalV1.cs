using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261028110000_ToolDispositionApprovalV1")]
public sealed class ToolDispositionApprovalV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_tool_approval_v1;
        CREATE TABLE cp6_tool_approval_v1.policies(
          sequence bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,tool_id uuid NOT NULL,id uuid NOT NULL,
          body text NOT NULL,digest text NOT NULL CHECK(digest ~ '^[0-9a-f]{64}$'),
          operation_id uuid NOT NULL,journal text NOT NULL CHECK(journal='Main.Wms.ToolDispositionApproval.operations.v1'),
          PRIMARY KEY(environment_id,tenant_id,id),UNIQUE(environment_id,tenant_id,tool_id,id),
          FOREIGN KEY(tenant_id,journal,operation_id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED);
        CREATE TABLE cp6_tool_approval_v1.submissions(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,tool_id uuid NOT NULL,id uuid NOT NULL,
          request_id uuid NOT NULL,policy_id uuid NOT NULL,actor_id uuid NOT NULL,operation_id uuid NOT NULL,
          intent_hash text NOT NULL CHECK(intent_hash ~ '^[0-9a-f]{64}$'),outbox_id uuid NOT NULL,
          payload bytea NOT NULL CHECK(octet_length(payload)<=1048576),payload_digest text NOT NULL,
          authorization_json text NOT NULL,journal text NOT NULL CHECK(journal='Main.Wms.ToolDispositionApproval.operations.v1'),
          PRIMARY KEY(environment_id,tenant_id,id),UNIQUE(environment_id,tenant_id,intent_hash),
          UNIQUE(environment_id,tenant_id,request_id,policy_id),UNIQUE(environment_id,tenant_id,id,outbox_id),
          FOREIGN KEY(tenant_id,request_id) REFERENCES cp6_tool_disposition_v1.facts(tenant_id,record_id),
          FOREIGN KEY(environment_id,tenant_id,tool_id,policy_id) REFERENCES cp6_tool_approval_v1.policies(environment_id,tenant_id,tool_id,id),
          FOREIGN KEY(tenant_id,journal,operation_id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED,
          CHECK(encode(sha256(payload),'hex')=payload_digest));
        CREATE TABLE cp6_tool_approval_v1.outbox(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,id uuid NOT NULL,submission_id uuid NOT NULL,
          payload bytea NOT NULL,payload_digest text NOT NULL,
          PRIMARY KEY(environment_id,tenant_id,id),UNIQUE(environment_id,tenant_id,submission_id),
          FOREIGN KEY(environment_id,tenant_id,submission_id,id) REFERENCES cp6_tool_approval_v1.submissions(environment_id,tenant_id,id,outbox_id) DEFERRABLE INITIALLY DEFERRED,
          CHECK(encode(sha256(payload),'hex')=payload_digest));
        ALTER TABLE cp6_tool_approval_v1.submissions ADD CONSTRAINT tool_approval_outbox_required
          FOREIGN KEY(environment_id,tenant_id,outbox_id) REFERENCES cp6_tool_approval_v1.outbox(environment_id,tenant_id,id) DEFERRABLE INITIALLY DEFERRED;
        CREATE FUNCTION cp6_tool_approval_v1.immutable() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN RAISE EXCEPTION 'original approval facts are immutable'; END $$;
        DO $$ DECLARE t text; BEGIN FOREACH t IN ARRAY ARRAY['policies','submissions','outbox'] LOOP
          EXECUTE format('CREATE TRIGGER tool_approval_immutable BEFORE UPDATE OR DELETE ON cp6_tool_approval_v1.%I FOR EACH ROW EXECUTE FUNCTION cp6_tool_approval_v1.immutable()',t);
          EXECUTE format('CREATE TRIGGER tool_approval_no_truncate BEFORE TRUNCATE ON cp6_tool_approval_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_tool_approval_v1.immutable()',t);
        END LOOP; END $$;
        CREATE FUNCTION cp6_tool_approval_v1.validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE b jsonb; r jsonb; d text; p bytea;
        BEGIN
          IF TG_TABLE_NAME='policies' THEN
            b:=NEW.body::jsonb;
            IF (b->>'environmentId') IS DISTINCT FROM NEW.environment_id::text OR (b->>'tenantId') IS DISTINCT FROM NEW.tenant_id::text
              OR (b->>'toolId') IS DISTINCT FROM NEW.tool_id::text OR (b->>'recordId') IS DISTINCT FROM NEW.id::text
              OR (b->'body'->>'operationId') IS DISTINCT FROM NEW.operation_id::text
              OR encode(sha256(convert_to(NEW.body,'UTF8')),'hex')<>NEW.digest THEN RAISE EXCEPTION 'approval policy identity mismatch'; END IF;
          ELSIF TG_TABLE_NAME='submissions' THEN
            b:=convert_from(NEW.payload,'UTF8')::jsonb;
            SELECT body_json::jsonb,body_digest INTO r,d FROM cp6_tool_disposition_v1.facts WHERE tenant_id=NEW.tenant_id AND record_id=NEW.request_id;
            IF r IS NULL OR r->>'kind'<>'REQUEST' OR (r->>'toolId') IS DISTINCT FROM NEW.tool_id::text
              OR (b->>'schema') IS DISTINCT FROM 'oa.app/2.0' OR (b->'scope'->>'environmentId') IS DISTINCT FROM NEW.environment_id::text
              OR (b->'scope'->>'tenantId') IS DISTINCT FROM NEW.tenant_id::text OR (b->>'submissionId') IS DISTINCT FROM NEW.id::text
              OR (b->>'commandId') IS DISTINCT FROM NEW.operation_id::text OR (b->>'ownerOutboxId') IS DISTINCT FROM NEW.outbox_id::text
              OR (b->>'starterId') IS DISTINCT FROM NEW.actor_id::text OR (b->'subject'->>'subjectId') IS DISTINCT FROM NEW.request_id::text
              OR (b->'subject'->>'businessOwner') IS DISTINCT FROM 'Main.Wms.ToolDisposition.v1'
              OR (b->'subject'->>'businessRevision') IS DISTINCT FROM '1'
              OR (b->'subject'->'contentDigest'->>'value') IS DISTINCT FROM d
              OR (b->'policy'->'ref'->>'id') IS DISTINCT FROM NEW.policy_id::text THEN RAISE EXCEPTION 'approval submission identity mismatch'; END IF;
          ELSE
            SELECT payload INTO p FROM cp6_tool_approval_v1.submissions WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND id=NEW.submission_id;
            IF p IS NULL OR p<>NEW.payload THEN RAISE EXCEPTION 'approval outbox must carry exact frozen bytes'; END IF;
          END IF;
          RETURN NEW;
        END $$;
        DO $$ DECLARE t text; BEGIN FOREACH t IN ARRAY ARRAY['policies','submissions','outbox'] LOOP
          EXECUTE format('CREATE TRIGGER tool_approval_validate BEFORE INSERT ON cp6_tool_approval_v1.%I FOR EACH ROW EXECUTE FUNCTION cp6_tool_approval_v1.validate()',t);
        END LOOP; END $$;
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Approval originals require reviewed forward recovery.");
}
