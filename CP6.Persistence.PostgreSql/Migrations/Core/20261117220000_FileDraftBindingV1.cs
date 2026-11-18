using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261117220000_FileDraftBindingV1")]
public sealed class FileDraftBindingV1 : Migration
{
    protected override void Up(MigrationBuilder migration) => migration.Sql("""
        CREATE TABLE cp6_file_content_v1.draft_sets (
          tenant_id uuid NOT NULL,draft_id uuid NOT NULL,actor_id uuid NOT NULL,
          revision bigint NOT NULL CHECK(revision>0),operation_id uuid NOT NULL,bound boolean NOT NULL,
          PRIMARY KEY(tenant_id,draft_id));
        CREATE TABLE cp6_file_content_v1.draft_items (
          tenant_id uuid NOT NULL,draft_id uuid NOT NULL,file_id uuid NOT NULL,ordinal bigint NOT NULL CHECK(ordinal>0),
          PRIMARY KEY(tenant_id,draft_id,file_id),UNIQUE(tenant_id,draft_id,ordinal),
          FOREIGN KEY(tenant_id,draft_id) REFERENCES cp6_file_content_v1.draft_sets,
          FOREIGN KEY(tenant_id,file_id) REFERENCES cp6_file_content_v1.contents);
        CREATE TABLE cp6_file_content_v1.draft_bindings (
          tenant_id uuid NOT NULL,draft_id uuid NOT NULL,file_id uuid NOT NULL,ordinal bigint NOT NULL CHECK(ordinal>0),
          target_id uuid NOT NULL,target_revision bigint NOT NULL,operation_id uuid NOT NULL,
          PRIMARY KEY(tenant_id,target_id,target_revision,file_id),UNIQUE(tenant_id,draft_id,file_id),
          FOREIGN KEY(tenant_id,draft_id,file_id) REFERENCES cp6_file_content_v1.draft_items,
          FOREIGN KEY(tenant_id,target_id,target_revision) REFERENCES cp6_engineering_revision_v1.working_versions);
        CREATE INDEX file_draft_items_by_content ON cp6_file_content_v1.draft_items(tenant_id,file_id);
        CREATE INDEX file_draft_bindings_by_draft ON cp6_file_content_v1.draft_bindings(tenant_id,draft_id);
        CREATE FUNCTION cp6_file_content_v1.guard_draft_set() RETURNS trigger
          LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF OLD.bound OR (NEW.tenant_id,NEW.draft_id,NEW.actor_id) IS DISTINCT FROM (OLD.tenant_id,OLD.draft_id,OLD.actor_id)
            OR NEW.revision<>OLD.revision+1 THEN
            RAISE EXCEPTION 'FILE_DRAFT_SET_IMMUTABLE' USING ERRCODE='23514';
          END IF;
          IF NEW.bound AND NOT EXISTS (
            SELECT FROM cp6_file_content_v1.draft_bindings b
            WHERE b.tenant_id=NEW.tenant_id AND b.draft_id=NEW.draft_id AND b.operation_id=NEW.operation_id
            GROUP BY b.target_id,b.target_revision
            HAVING count(*)=(SELECT count(*) FROM cp6_file_content_v1.draft_items d
              WHERE d.tenant_id=NEW.tenant_id AND d.draft_id=NEW.draft_id)
          ) THEN
            RAISE EXCEPTION 'FILE_DRAFT_BINDING_INCOMPLETE' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER file_draft_advance BEFORE UPDATE ON cp6_file_content_v1.draft_sets
          FOR EACH ROW EXECUTE FUNCTION cp6_file_content_v1.guard_draft_set();
        CREATE FUNCTION cp6_file_content_v1.guard_draft_item() RETURNS trigger
          LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF EXISTS(SELECT FROM cp6_file_content_v1.draft_sets s
              WHERE s.tenant_id=NEW.tenant_id AND s.draft_id=NEW.draft_id AND s.bound) THEN
              RAISE EXCEPTION 'FILE_DRAFT_SET_IMMUTABLE' USING ERRCODE='23514';
            END IF;
            RETURN NEW;
          END IF;
          IF EXISTS(SELECT FROM cp6_file_content_v1.draft_sets s
            WHERE s.tenant_id=OLD.tenant_id AND s.draft_id=OLD.draft_id AND s.bound) THEN
            RAISE EXCEPTION 'FILE_DRAFT_SET_IMMUTABLE' USING ERRCODE='23514';
          END IF;
          RETURN OLD;
        END $$;
        CREATE TRIGGER file_draft_items_guard BEFORE INSERT OR DELETE ON cp6_file_content_v1.draft_items
          FOR EACH ROW EXECUTE FUNCTION cp6_file_content_v1.guard_draft_item();
        CREATE TRIGGER file_draft_items_no_update BEFORE UPDATE OR TRUNCATE ON cp6_file_content_v1.draft_items
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER file_draft_sets_permanent BEFORE DELETE OR TRUNCATE ON cp6_file_content_v1.draft_sets
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER file_draft_bindings_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_file_content_v1.draft_bindings
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        REVOKE ALL ON TABLE cp6_file_content_v1.draft_sets,cp6_file_content_v1.draft_items,cp6_file_content_v1.draft_bindings FROM PUBLIC;
        REVOKE ALL ON FUNCTION cp6_file_content_v1.guard_draft_set(),cp6_file_content_v1.guard_draft_item() FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migration) =>
        throw new NotSupportedException("文件草稿的原操作和正式绑定需要前向恢复。");
}
