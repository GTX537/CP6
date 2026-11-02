using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261102151000_DefinitionCloneV2")]
public sealed class DefinitionCloneV2 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE cp6_workflow_authoring_v2.heads ADD COLUMN cloned_from_version_id uuid,
          ADD FOREIGN KEY(tenant_id,environment_id,cloned_from_version_id) REFERENCES cp6_workflow_authoring_v2.versions(tenant_id,environment_id,id);
        CREATE FUNCTION cp6_workflow_authoring_v2.clone_origin_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF NEW.cloned_from_version_id IS DISTINCT FROM OLD.cloned_from_version_id THEN
            RAISE EXCEPTION 'CLONE_ORIGIN_IMMUTABLE' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER clone_origin_immutable BEFORE UPDATE ON cp6_workflow_authoring_v2.heads
          FOR EACH ROW EXECUTE FUNCTION cp6_workflow_authoring_v2.clone_origin_guard();
        ALTER TABLE cp6_workflow_authoring_v2.commands DROP CONSTRAINT commands_operation_check,
          ADD CHECK(operation IN('FLOW_SAVE_DRAFT','FLOW_PUBLISH','FLOW_CREATE_DRAFT','FLOW_SET_ACTIVATION','FLOW_CLONE'));
        ALTER TABLE cp6_workflow_authoring_v2.audit DROP CONSTRAINT audit_operation_check,
          ADD CHECK(operation IN('FLOW_SAVE_DRAFT','FLOW_PUBLISH','FLOW_CREATE_DRAFT','FLOW_SET_ACTIVATION','FLOW_CLONE'));
        CREATE FUNCTION cp6_workflow_authoring_v2.clone_ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_workflow_authoring_v2.activation_ready() AND EXISTS(SELECT FROM pg_trigger
            WHERE tgrelid='cp6_workflow_authoring_v2.heads'::regclass AND tgname='clone_origin_immutable' AND tgenabled='O' AND tgtype=19
              AND tgfoid='cp6_workflow_authoring_v2.clone_origin_guard()'::regprocedure)
        $$;
        ALTER TABLE cp6_form_authoring_v2.heads ADD COLUMN cloned_from_version_id uuid,
          ADD FOREIGN KEY(tenant_id,environment_id,cloned_from_version_id) REFERENCES cp6_form_authoring_v2.versions(tenant_id,environment_id,id);
        CREATE FUNCTION cp6_form_authoring_v2.clone_origin_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF NEW.cloned_from_version_id IS DISTINCT FROM OLD.cloned_from_version_id THEN
            RAISE EXCEPTION 'CLONE_ORIGIN_IMMUTABLE' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER clone_origin_immutable BEFORE UPDATE ON cp6_form_authoring_v2.heads
          FOR EACH ROW EXECUTE FUNCTION cp6_form_authoring_v2.clone_origin_guard();
        ALTER TABLE cp6_form_authoring_v2.commands DROP CONSTRAINT commands_operation_check,
          ADD CHECK(operation IN('FORM_SAVE_DRAFT','FORM_PUBLISH','FORM_CREATE_DRAFT','FORM_SET_ACTIVATION','FORM_CLONE'));
        ALTER TABLE cp6_form_authoring_v2.audit DROP CONSTRAINT audit_operation_check,
          ADD CHECK(operation IN('FORM_SAVE_DRAFT','FORM_PUBLISH','FORM_CREATE_DRAFT','FORM_SET_ACTIVATION','FORM_CLONE'));
        CREATE FUNCTION cp6_form_authoring_v2.clone_ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_form_authoring_v2.activation_ready() AND EXISTS(SELECT FROM pg_trigger
            WHERE tgrelid='cp6_form_authoring_v2.heads'::regclass AND tgname='clone_origin_immutable' AND tgenabled='O' AND tgtype=19
              AND tgfoid='cp6_form_authoring_v2.clone_origin_guard()'::regprocedure)
        $$;
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Forward-only definition history.");
}
