using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261109204600_IamUserMembershipTenantReferenceV1")]
public sealed class IamUserMembershipTenantReferenceV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE public."Sys_Users"
            ADD CONSTRAINT "AK_Sys_Users_TenantId_Id" UNIQUE ("TenantId", "Id");

        -- The IAM Owner can stage the user and memberships in either order in one transaction.
        ALTER TABLE public."Sys_UserRole"
            ADD CONSTRAINT "FK_Sys_UserRole_TenantUser"
            FOREIGN KEY ("TenantId", "UserId") REFERENCES public."Sys_Users" ("TenantId", "Id")
            ON UPDATE NO ACTION ON DELETE NO ACTION
            DEFERRABLE INITIALLY DEFERRED;
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("IAM membership tenant references require forward recovery.");
}
