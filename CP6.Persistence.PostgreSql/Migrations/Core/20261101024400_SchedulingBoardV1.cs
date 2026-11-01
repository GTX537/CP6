using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261101024400_SchedulingBoardV1")]
public sealed class SchedulingBoardV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE TABLE cp6_scheduling_v1.board_sources(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,root_id uuid NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,root_id));
        REVOKE ALL ON cp6_scheduling_v1.board_sources FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Forward-only board source history and captures.");
}
