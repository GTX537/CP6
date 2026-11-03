using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261103131500_WorkflowCommentUnicodeCapacityV2")]
public sealed class WorkflowCommentUnicodeCapacityV2:Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // bpchar has no typmod here. Its raw bytes retain trailing spaces;
        // casting to text before measuring would silently discard those characters.
        foreach(var table in new[]{"Wf_FlowTask","Wf_FlowHistory","Wf_FlowFormTo"})
            migrationBuilder.Sql($"""
                ALTER TABLE public."{table}"
                  DROP CONSTRAINT "CK_{table}_Comment_Utf16Capacity",
                  ADD CONSTRAINT "CK_{table}_Comment_UnicodeScalarCapacity"
                    CHECK(char_length(convert_from(pg_catalog.bpcharsend("Comment"), 'UTF8')) <= 1000);
                """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
        =>throw new NotSupportedException("Do not shrink accepted workflow comments; use forward recovery.");
}
