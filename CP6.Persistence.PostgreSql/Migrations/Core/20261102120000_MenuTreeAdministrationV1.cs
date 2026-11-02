using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261102120000_MenuTreeAdministrationV1")]
public sealed class MenuTreeAdministrationV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE public."Sys_Menus" IN SHARE ROW EXCLUSIVE MODE;
        CREATE SCHEMA cp6_menu_tree_v1;
        REVOKE ALL ON SCHEMA cp6_menu_tree_v1 FROM PUBLIC;
        CREATE TABLE cp6_menu_tree_v1.root (
          singleton boolean PRIMARY KEY CHECK(singleton), revision bigint NOT NULL CHECK(revision>0)
        );
        INSERT INTO cp6_menu_tree_v1.root VALUES(true,1);
        CREATE TABLE cp6_menu_tree_v1.commands (
          operation_hash bytea PRIMARY KEY CHECK(octet_length(operation_hash)=32),
          operation_bytes bytea NOT NULL,input_bytes bytea NOT NULL,
          actor_id uuid NOT NULL,actor_tenant uuid NOT NULL,result_bytes bytea NOT NULL
        );
        CREATE FUNCTION cp6_menu_tree_v1.immutable() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN RAISE EXCEPTION 'MENU_TREE_HISTORY_IMMUTABLE' USING ERRCODE='55000'; END $$;
        CREATE TRIGGER cp6_menu_tree_history_v1 BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_menu_tree_v1.commands
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_menu_tree_v1.immutable();
        CREATE TRIGGER cp6_menu_tree_root_keep_v1 BEFORE DELETE OR TRUNCATE ON cp6_menu_tree_v1.root
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_menu_tree_v1.immutable();
        CREATE FUNCTION cp6_menu_tree_v1.guard_revision() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF NEW.singleton IS DISTINCT FROM OLD.singleton OR NEW.revision<>OLD.revision+1 THEN
            RAISE EXCEPTION 'MENU_TREE_REVISION_INVALID' USING ERRCODE='55000';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER cp6_menu_tree_revision_guard_v1 BEFORE UPDATE ON cp6_menu_tree_v1.root
          FOR EACH ROW EXECUTE FUNCTION cp6_menu_tree_v1.guard_revision();
        CREATE FUNCTION cp6_menu_tree_v1.changed() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          UPDATE cp6_menu_tree_v1.root SET revision=revision+1 WHERE singleton;
          IF NOT FOUND THEN RAISE EXCEPTION 'MENU_TREE_VERSION_UNAVAILABLE' USING ERRCODE='55000'; END IF;
          RETURN NULL;
        END $$;
        CREATE TRIGGER cp6_menu_tree_changed_v1 AFTER INSERT OR UPDATE OR DELETE OR TRUNCATE ON public."Sys_Menus"
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_menu_tree_v1.changed();
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_menu_tree_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_menu_tree_v1 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Menu tree revisions and originals require forward recovery.");
}
