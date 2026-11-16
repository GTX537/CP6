using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261116014000_WarehouseQuickPinSealIntentV1")]
public sealed class WarehouseQuickPinSealIntentV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        ALTER TABLE cp6_authentication_v1.pending_crm_user_seals
          DROP CONSTRAINT pending_crm_user_seals_kind_check,
          ADD CONSTRAINT pending_crm_user_seals_kind_check
            CHECK (kind IN ('user-update','user-roles','user-org','self-change-password','admin-reset-2fa',
                            'self-setup-2fa','self-enroll-2fa','self-disable-2fa','challenge-setup-2fa','challenge-enroll-2fa',
                            'privacy-disposition','wms-set-quick-pin'));
        """);

    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Pending CRM QuickPIN seals require forward recovery.");
}
