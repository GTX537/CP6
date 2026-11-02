using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261102080000_CodeGenMetadataV1")]
public sealed class CodeGenMetadataV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_codegen_metadata_v1;
        CREATE TABLE cp6_codegen_metadata_v1.documents(
          tenant_id uuid NOT NULL, metadata_id uuid NOT NULL,
          entity_name bpchar COLLATE "cp6_ci_as_provider_v1" NOT NULL,
          revision bigint NOT NULL CHECK(revision>0), PRIMARY KEY(tenant_id,metadata_id),
          CONSTRAINT codegen_metadata_name UNIQUE(tenant_id,entity_name));
        CREATE TABLE cp6_codegen_metadata_v1.versions(
          tenant_id uuid NOT NULL,metadata_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),body bytea NOT NULL,
          PRIMARY KEY(tenant_id,metadata_id,revision),FOREIGN KEY(tenant_id,metadata_id)
            REFERENCES cp6_codegen_metadata_v1.documents DEFERRABLE INITIALLY DEFERRED);
        ALTER TABLE cp6_codegen_metadata_v1.documents ADD FOREIGN KEY(tenant_id,metadata_id,revision)
          REFERENCES cp6_codegen_metadata_v1.versions DEFERRABLE INITIALLY DEFERRED;
        CREATE TABLE cp6_codegen_metadata_v1.commands(
          tenant_id uuid NOT NULL,metadata_id uuid NOT NULL,operation_hash bytea NOT NULL CHECK(octet_length(operation_hash)=32),
          operation_bytes bytea NOT NULL,input_bytes bytea NOT NULL,actor_id uuid NOT NULL,revision bigint NOT NULL,result_bytes bytea NOT NULL,
          PRIMARY KEY(tenant_id,metadata_id,operation_hash),UNIQUE(tenant_id,metadata_id,revision),
          FOREIGN KEY(tenant_id,metadata_id,revision) REFERENCES cp6_codegen_metadata_v1.versions);
        CREATE FUNCTION cp6_codegen_metadata_v1.immutable() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'CODEGEN_HISTORY_IMMUTABLE' USING ERRCODE='23514'; END $$;
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON cp6_codegen_metadata_v1.versions
          FOR EACH ROW EXECUTE FUNCTION cp6_codegen_metadata_v1.immutable();
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON cp6_codegen_metadata_v1.commands
          FOR EACH ROW EXECUTE FUNCTION cp6_codegen_metadata_v1.immutable();
        CREATE TRIGGER no_delete BEFORE DELETE ON cp6_codegen_metadata_v1.documents
          FOR EACH ROW EXECUTE FUNCTION cp6_codegen_metadata_v1.immutable();
        CREATE TRIGGER no_truncate BEFORE TRUNCATE ON cp6_codegen_metadata_v1.documents
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_codegen_metadata_v1.immutable();
        CREATE TRIGGER no_truncate BEFORE TRUNCATE ON cp6_codegen_metadata_v1.versions
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_codegen_metadata_v1.immutable();
        CREATE TRIGGER no_truncate BEFORE TRUNCATE ON cp6_codegen_metadata_v1.commands
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_codegen_metadata_v1.immutable();
        CREATE FUNCTION cp6_codegen_metadata_v1.advance() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.revision<>1 THEN RAISE EXCEPTION 'CODEGEN_REVISION_INVALID' USING ERRCODE='23514'; END IF;
          ELSIF NEW.tenant_id<>OLD.tenant_id OR NEW.metadata_id<>OLD.metadata_id OR NEW.revision<>OLD.revision+1 THEN
            RAISE EXCEPTION 'CODEGEN_REVISION_INVALID' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER advance BEFORE INSERT OR UPDATE ON cp6_codegen_metadata_v1.documents
          FOR EACH ROW EXECUTE FUNCTION cp6_codegen_metadata_v1.advance();
        CREATE FUNCTION cp6_codegen_metadata_v1.complete_version() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NOT EXISTS(SELECT FROM cp6_codegen_metadata_v1.commands WHERE tenant_id=NEW.tenant_id
            AND metadata_id=NEW.metadata_id AND revision=NEW.revision) THEN
            RAISE EXCEPTION 'CODEGEN_COMMAND_REQUIRED' USING ERRCODE='23514'; END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER complete_version AFTER INSERT ON cp6_codegen_metadata_v1.versions
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_codegen_metadata_v1.complete_version();
        CREATE FUNCTION cp6_codegen_metadata_v1.legacy_write() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF EXISTS(SELECT FROM cp6_codegen_metadata_v1.documents WHERE tenant_id=NEW."TenantId" AND entity_name=NEW."EntityName") THEN
            RAISE EXCEPTION 'CODEGEN_METADATA_OWNER_REQUIRED' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER codegen_owner BEFORE INSERT OR UPDATE ON public."Pub_GenTable"
          FOR EACH ROW EXECUTE FUNCTION cp6_codegen_metadata_v1.legacy_write();
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("CodeGen versions and original operations are forward-only.");
}
