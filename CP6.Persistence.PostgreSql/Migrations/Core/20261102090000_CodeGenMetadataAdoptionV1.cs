using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261102090000_CodeGenMetadataAdoptionV1")]
public sealed class CodeGenMetadataAdoptionV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE cp6_codegen_metadata_v1.adoptions(
          tenant_id uuid NOT NULL, metadata_id uuid NOT NULL, source_fingerprint text NOT NULL,
          PRIMARY KEY(tenant_id,metadata_id), FOREIGN KEY(tenant_id,metadata_id)
            REFERENCES cp6_codegen_metadata_v1.documents);
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON cp6_codegen_metadata_v1.adoptions
          FOR EACH ROW EXECUTE FUNCTION cp6_codegen_metadata_v1.immutable();
        CREATE TRIGGER no_truncate BEFORE TRUNCATE ON cp6_codegen_metadata_v1.adoptions
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_codegen_metadata_v1.immutable();
        CREATE FUNCTION cp6_codegen_metadata_v1.frozen_legacy() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE old_id uuid; new_id uuid;
        BEGIN
          IF TG_OP<>'INSERT' THEN
            IF TG_TABLE_NAME='Pub_GenTable' THEN old_id:=OLD."Id"; ELSE old_id:=OLD."GenTableId"; END IF;
            IF EXISTS(SELECT FROM cp6_codegen_metadata_v1.adoptions WHERE tenant_id=OLD."TenantId" AND metadata_id=old_id) THEN
              RAISE EXCEPTION 'CODEGEN_METADATA_OWNER_REQUIRED' USING ERRCODE='23514'; END IF;
          END IF;
          IF TG_OP<>'DELETE' THEN
            IF TG_TABLE_NAME='Pub_GenTable' THEN new_id:=NEW."Id"; ELSE new_id:=NEW."GenTableId"; END IF;
            IF EXISTS(SELECT FROM cp6_codegen_metadata_v1.adoptions WHERE tenant_id=NEW."TenantId" AND metadata_id=new_id) THEN
              RAISE EXCEPTION 'CODEGEN_METADATA_OWNER_REQUIRED' USING ERRCODE='23514'; END IF;
            RETURN NEW;
          END IF;
          RETURN OLD;
        END $$;
        CREATE TRIGGER codegen_frozen BEFORE INSERT OR UPDATE OR DELETE ON public."Pub_GenTable"
          FOR EACH ROW EXECUTE FUNCTION cp6_codegen_metadata_v1.frozen_legacy();
        CREATE TRIGGER codegen_frozen BEFORE INSERT OR UPDATE OR DELETE ON public."Pub_GenColumn"
          FOR EACH ROW EXECUTE FUNCTION cp6_codegen_metadata_v1.frozen_legacy();
        CREATE FUNCTION cp6_codegen_metadata_v1.frozen_truncate() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF EXISTS(SELECT FROM cp6_codegen_metadata_v1.adoptions) THEN
            RAISE EXCEPTION 'CODEGEN_METADATA_OWNER_REQUIRED' USING ERRCODE='23514'; END IF;
          RETURN NULL;
        END $$;
        CREATE TRIGGER codegen_no_truncate BEFORE TRUNCATE ON public."Pub_GenTable"
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_codegen_metadata_v1.frozen_truncate();
        CREATE TRIGGER codegen_no_truncate BEFORE TRUNCATE ON public."Pub_GenColumn"
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_codegen_metadata_v1.frozen_truncate();
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Legacy metadata adoption is forward-only.");
}
