using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261123124500_LegacyFormHistorySnapshotV1")]
public sealed class LegacyFormHistorySnapshotV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        -- 存量行在安装查询能力时已经存在。新增行记录原插入事务，后续状态更新不改变入库资格。
        ALTER TABLE public."Wf_FlowInstance" ADD COLUMN legacy_query_insert_xid xid8 NOT NULL DEFAULT '0'::xid8;
        ALTER TABLE public."Wf_FlowInstance" ALTER COLUMN legacy_query_insert_xid SET DEFAULT pg_current_xact_id();
        """);

    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("旧流程查询截点采用前向迁移。");
}
