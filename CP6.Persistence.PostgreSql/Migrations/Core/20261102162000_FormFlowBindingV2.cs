using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261102162000_FormFlowBindingV2")]
public sealed class FormFlowBindingV2 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE cp6_workflow_authoring_v2.activation_current
          ADD COLUMN form_head_id uuid, ADD COLUMN form_version_id uuid,
          ADD CONSTRAINT activation_form_pair CHECK((form_head_id IS NULL)=(form_version_id IS NULL)),
          ADD CONSTRAINT activation_form_version FOREIGN KEY(tenant_id,environment_id,form_head_id,form_version_id)
            REFERENCES cp6_form_authoring_v2.versions(tenant_id,environment_id,head_id,id);
        CREATE FUNCTION cp6_workflow_authoring_v2.activation_form_binding() RETURNS trigger
          LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE reference jsonb;
        BEGIN
          -- The published snapshot owns this projection. Caller-supplied binding columns are never authoritative.
          NEW.form_head_id:=NULL; NEW.form_version_id:=NULL;
          IF NEW.published_version_id IS NULL THEN RETURN NEW; END IF;
          SELECT r.value INTO reference FROM cp6_workflow_authoring_v2.versions v,
            LATERAL jsonb_array_elements(v.snapshot_json::jsonb->'dependencyRefs') r(value)
            WHERE v.tenant_id=NEW.tenant_id AND v.environment_id=NEW.environment_id AND v.head_id=NEW.head_id
              AND v.id=NEW.published_version_id AND r.value->>'kind'='FORM' AND r.value->>'location'='formKey';
          IF reference IS NOT NULL THEN
            SELECT f.head_id,f.id INTO NEW.form_head_id,NEW.form_version_id
              FROM cp6_form_authoring_v2.versions f JOIN cp6_form_authoring_v2.heads h
                ON h.tenant_id=f.tenant_id AND h.environment_id=f.environment_id AND h.id=f.head_id
              WHERE f.tenant_id=NEW.tenant_id AND f.environment_id=NEW.environment_id
                AND f.id=(reference->>'version')::uuid AND h.form_key=reference->>'reference';
            IF NOT FOUND THEN RAISE EXCEPTION 'DEPENDENCY_VERSION_NOT_AVAILABLE' USING ERRCODE='23514'; END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER activation_form_binding BEFORE INSERT OR UPDATE ON cp6_workflow_authoring_v2.activation_current
          FOR EACH ROW EXECUTE FUNCTION cp6_workflow_authoring_v2.activation_form_binding();
        -- Only derive the new columns, under this migration's table lock and transaction.
        -- Existing activation revisions/history are untouched. Conflicting legacy activations abort the migration.
        ALTER TABLE cp6_workflow_authoring_v2.activation_current DISABLE TRIGGER activation_guard;
        UPDATE cp6_workflow_authoring_v2.activation_current SET form_head_id=NULL;
        ALTER TABLE cp6_workflow_authoring_v2.activation_current ENABLE TRIGGER activation_guard;
        CREATE UNIQUE INDEX ux_activation_form ON cp6_workflow_authoring_v2.activation_current(tenant_id,environment_id,form_head_id)
          WHERE enabled AND form_head_id IS NOT NULL;
        CREATE FUNCTION cp6_workflow_authoring_v2.form_binding_ready() RETURNS boolean
          LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_workflow_authoring_v2.activation_ready()
            AND EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid='cp6_workflow_authoring_v2.activation_current'::regclass
              AND t.tgname='activation_form_binding' AND t.tgfoid='cp6_workflow_authoring_v2.activation_form_binding()'::regprocedure
              AND t.tgenabled IN('O','A') AND t.tgtype=23 AND t.tgqual IS NULL AND t.tgnargs=0 AND NOT t.tgisinternal)
            AND EXISTS(SELECT FROM pg_index i WHERE i.indexrelid=to_regclass('cp6_workflow_authoring_v2.ux_activation_form')
              AND i.indrelid='cp6_workflow_authoring_v2.activation_current'::regclass AND i.indisunique AND i.indisvalid AND i.indisready)
        $$;
        REVOKE ALL ON FUNCTION cp6_workflow_authoring_v2.activation_form_binding(),cp6_workflow_authoring_v2.form_binding_ready() FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Activation bindings require forward recovery.");
}
