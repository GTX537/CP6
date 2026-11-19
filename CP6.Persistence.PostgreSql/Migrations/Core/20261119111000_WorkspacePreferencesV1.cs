using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261119111000_WorkspacePreferencesV1")]
public sealed class WorkspacePreferencesV1 : Migration
{
    protected override void Up(MigrationBuilder migration) => migration.Sql("""
        CREATE SCHEMA cp6_workspace_v1;

        CREATE TABLE cp6_workspace_v1.preferences(
            tenant_id uuid NOT NULL,
            subject_id uuid NOT NULL,
            revision bigint NOT NULL CHECK (revision > 0),
            visible_metrics text[] NOT NULL CHECK (cardinality(visible_metrics) <= 8),
            PRIMARY KEY (tenant_id, subject_id)
        );

        CREATE TABLE cp6_workspace_v1.preference_commands(
            tenant_id uuid NOT NULL,
            subject_id uuid NOT NULL,
            operation_ref uuid NOT NULL,
            expected_revision bigint NOT NULL CHECK (expected_revision >= 0),
            visible_metrics text[] NOT NULL CHECK (cardinality(visible_metrics) <= 8),
            applied_revision bigint NOT NULL CHECK (applied_revision > 0),
            applied_at_utc timestamptz NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY (tenant_id, subject_id, operation_ref)
        );

        CREATE TRIGGER workspace_preference_root
            BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_workspace_v1.preferences
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER workspace_preference_receipts_root
            BEFORE INSERT ON cp6_workspace_v1.preference_commands
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER workspace_preference_receipts_immutable
            BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_workspace_v1.preference_commands
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        """);

    protected override void Down(MigrationBuilder migration)
        => throw new NotSupportedException("工作台个性配置和原操作账本只允许前向迁移。");
}
