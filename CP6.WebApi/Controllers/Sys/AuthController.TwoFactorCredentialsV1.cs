using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Localization;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Sys;

public partial class AuthController
{
    private Task<IActionResult> PgSetupSelf() => IamAdministrationHttpV1.ExecuteLegacyCredential(this, async () =>
    {
        var owner = await IamAdministrationHttpV1.WriteLegacyCredential(HttpContext, null, null,
            user =>
            {
                if (user.TwoFactorEnabled) throw new BizException("E-SEC-017");
                return Task.CompletedTask;
            },
            (user, _) => { _2fa.BeginEnrollment(user); return Task.CompletedTask; },
            factor: new("self-setup-2fa"));
        if (owner is null) throw IamAdministrationV1.Error(401, "REAUTH_REQUIRED");

        var secret = await _context.Database.SqlQuery<string>($"""
            SELECT u."TwoFactorSecret" AS "Value"
            FROM public."Sys_Users" u
            JOIN cp6_authentication_v1.preparation_versions p ON p.user_id=u."Id"
            WHERE u."Id"={owner.UserId} AND u."TenantId"={_context.CurrentTenantId}
                AND p.revision={owner.ResultPreparationRevision} AND u."TwoFactorSecret" IS NOT NULL
            """).SingleOrDefaultAsync(HttpContext.RequestAborted);
        if (secret is null) throw IamAdministrationV1.Error(503, "IAM_OPERATION_UNKNOWN");
        var uri = HttpContext.RequestServices.GetRequiredService<ITotpService>().BuildOtpAuthUri(secret, owner.UserName);
        return Ok(new { otpauthUri = uri, secret });
    });

    private Task<IActionResult> PgEnrollSelf(TwoFactorCodeRequest request) => IamAdministrationHttpV1.ExecuteLegacyCredential(this, async () =>
    {
        var owner = await IamAdministrationHttpV1.WriteLegacyCredential(HttpContext, null, null,
            static _ => Task.CompletedTask,
            (user, plan) =>
            {
                TwoFactorService.ApplyEnrollment(user, plan.ChangedAt, plan.ResetEpoch!.Value);
                return Task.CompletedTask;
            },
            factor: new("self-enroll-2fa", "totp", request.Code,
                user =>
                {
                    if (!_2fa.VerifyTotp(user, request.Code)) throw new BizException("E-SEC-011");
                    return Task.CompletedTask;
                },
                (user, ct) => SaveFactorAudit(user, SecurityEventType.TwoFactorEnrolled, null, null, null, ct)));
        if (owner is null) throw IamAdministrationV1.Error(401, "REAUTH_REQUIRED");
        return Ok(new { code = 0 });
    });

    private Task<IActionResult> PgDisableSelf(DisableTwoFactorRequest request) => IamAdministrationHttpV1.ExecuteLegacyCredential(this, async () =>
    {
        var email = string.Equals(request.Method, "email", StringComparison.OrdinalIgnoreCase);
        var owner = await IamAdministrationHttpV1.WriteLegacyCredential(HttpContext, null, null,
            user =>
            {
                if (!_hasher.Verify(request.CurrentPassword, user.Password)) throw new BizException("E-SEC-006");
                if (_2fa.ResolveTenantMode(user.TenantId) == 2) throw new BizException("E-SEC-019");
                return Task.CompletedTask;
            },
            (user, plan) => { TwoFactorService.ResetCredentials(user, plan.ResetEpoch); return Task.CompletedTask; },
            factor: new("self-disable-2fa", email ? "email" : "totp", request.Code, async user =>
            {
                var verified = email ? await _2fa.VerifyEmailOtpAsync("self:" + user.Id, request.Code) : _2fa.VerifyTotp(user, request.Code);
                if (!verified) throw new BizException("E-SEC-011");
            }, async (user, ct) =>
            {
                await SaveFactorAudit(user, SecurityEventType.TwoFactorReset, "self-disable", null, null, ct);
                await SaveFactorAudit(user, SecurityEventType.TwoFactorReset, "self-disable", ClientIp, ClientUa, ct);
            }));
        if (owner is null) throw IamAdministrationV1.Error(401, "REAUTH_REQUIRED");
        return Ok(new { code = 0 });
    });

    private Task SaveFactorAudit(Sys_User user, SecurityEventType type, string? reason, string? ip, string? agent, CancellationToken ct)
        => SecurityAuditService.SaveWithinRootAsync(_context,
            SecurityAuditService.CreateRecord(type, user.Id, user.UserName, null, ip, agent, reason), bestEffort: true, ct);
}
