using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261103124500_AppTaskWfcAliasV2")]
public sealed class AppTaskWfcAliasV2:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        ALTER TABLE cp6_approval_decision_v2.commands
          ADD COLUMN wfc_raw_body bytea,
          ADD COLUMN wfc_digest text,
          ADD CONSTRAINT app_wfc_alias_complete CHECK(
            (wfc_raw_body IS NULL AND wfc_digest IS NULL) OR
            (wfc_raw_body IS NOT NULL AND wfc_digest IS NOT NULL AND length(wfc_raw_body)>0 AND wfc_digest ~ '^[0-9a-f]{64}$'));
        -- command_guard already protects the entire original row, excluding only the
        -- existing completion/result/audit fields. Alias bytes share its immutable TxD.
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Accepted AppCommand aliases require forward recovery.");
}
