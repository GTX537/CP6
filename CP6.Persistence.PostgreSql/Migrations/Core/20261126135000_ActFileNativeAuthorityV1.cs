using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261126135000_ActFileNativeAuthorityV1")]
public sealed class ActFileNativeAuthorityV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- 原件预检可能转为隔离写。先取 Main 排他行锁，避免后续从共享锁升级。
        CREATE FUNCTION cp6_act_file_v1.hold_source_for_integrity_v1(t uuid)
        RETURNS TABLE(store_id uuid,epoch bigint,receipt uuid)
        LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $$
            SELECT s.store_id,s.epoch,s.receipt FROM cp6_act_file_v1.sources s WHERE s.tenant_id=t FOR UPDATE
        $$;
        REVOKE ALL ON FUNCTION cp6_act_file_v1.hold_source_for_integrity_v1(uuid) FROM PUBLIC;

        -- FILE 包含版本及产品的对象范围。相关部门绑定撤销也要使既有证明失效。
        CREATE TRIGGER act_file_scope_insert AFTER INSERT ON cp6_operations_owner_v1.department_mappings
          FOR EACH ROW WHEN (NEW.object_kind IN ('plm-engineering-revision','plm-engineering-product'))
          EXECUTE FUNCTION cp6_act_file_v1.source_changed_v1('tenant_id');
        CREATE TRIGGER act_file_scope_update AFTER UPDATE ON cp6_operations_owner_v1.department_mappings
          FOR EACH ROW WHEN (OLD.object_kind IN ('plm-engineering-revision','plm-engineering-product')
                             OR NEW.object_kind IN ('plm-engineering-revision','plm-engineering-product'))
          EXECUTE FUNCTION cp6_act_file_v1.source_changed_v1('tenant_id');
        CREATE TRIGGER act_file_scope_delete AFTER DELETE ON cp6_operations_owner_v1.department_mappings
          FOR EACH ROW WHEN (OLD.object_kind IN ('plm-engineering-revision','plm-engineering-product'))
          EXECUTE FUNCTION cp6_act_file_v1.source_changed_v1('tenant_id');
        CREATE TRIGGER act_file_scope_truncate AFTER TRUNCATE ON cp6_operations_owner_v1.department_mappings
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_act_file_v1.source_changed_v1();
        ALTER TABLE cp6_operations_owner_v1.department_mappings ENABLE ALWAYS TRIGGER act_file_scope_insert;
        ALTER TABLE cp6_operations_owner_v1.department_mappings ENABLE ALWAYS TRIGGER act_file_scope_update;
        ALTER TABLE cp6_operations_owner_v1.department_mappings ENABLE ALWAYS TRIGGER act_file_scope_delete;
        ALTER TABLE cp6_operations_owner_v1.department_mappings ENABLE ALWAYS TRIGGER act_file_scope_truncate;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("ACT 文件对象权限参加规则只能前向恢复。");
}
