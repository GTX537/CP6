using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261116020000_SsoIdentityLinkCrmSealIntentV1")]
public sealed class SsoIdentityLinkCrmSealIntentV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        ALTER TABLE cp6_authentication_v1.pending_crm_user_seals
          DROP CONSTRAINT pending_crm_user_seals_kind_check,
          ADD CONSTRAINT pending_crm_user_seals_kind_check
            CHECK (kind IN ('user-update','user-roles','user-org','self-change-password','admin-reset-2fa',
                            'self-setup-2fa','self-enroll-2fa','self-disable-2fa','challenge-setup-2fa','challenge-enroll-2fa',
                            'privacy-disposition','wms-set-quick-pin','login-password-lockout','login-factor-lockout','sso-identity-link'));
        ALTER TABLE cp6_authentication_v1.sso_states
          ADD COLUMN original_mapping_ref text REFERENCES cp6_authentication_v1.sso_mapping_intents(operation_ref),
          ADD CONSTRAINT sso_states_distinct_mapping_ref CHECK (original_mapping_ref<>state_hash);
        """);

    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Pending CRM SSO links require forward recovery.");
}
