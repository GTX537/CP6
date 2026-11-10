using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261110020400_IamMembershipRoleTenantReferenceV1")]
public sealed class IamMembershipRoleTenantReferenceV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE public."Sys_UserRole"
            ADD CONSTRAINT "FK_Sys_UserRole_TenantRole"
            FOREIGN KEY ("TenantId", "RoleId") REFERENCES public."Sys_Roles" ("TenantId", "RoleId")
            ON UPDATE NO ACTION ON DELETE NO ACTION
            DEFERRABLE INITIALLY DEFERRED;
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("IAM membership role references require forward recovery.");
}
