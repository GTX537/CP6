using System.Text;
using System.Text.Json;
using CP6.Core.Auth;
using CP6.Core.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Platform;

public partial class TenantAdminService
{
    public Task<TenantCreationTemplateV1> ReadCreationTemplateAsync(TenantManagementSessionV1 session, CancellationToken ct = default)
        => ManagementTransaction(false, session, (_, token) => CreationTemplate(token), ct);

    private async Task<TenantCreationTemplateV1> CreationTemplate(CancellationToken ct)
    {
        var source = TenantContext.DefaultTenant;
        if (!await _db.Sys_Roles.IgnoreQueryFilters().AnyAsync(x => x.TenantId == source && x.RoleId == 1 && x.Enable, ct))
            throw new TenantManagementExceptionV1("TENANT_TEMPLATE_NOT_READY");
        var ids = await _db.Sys_RoleMenus.IgnoreQueryFilters().Where(x => x.TenantId == source && x.RoleId == 1)
            .Select(x => x.MenuId).Distinct().OrderBy(x => x).ToArrayAsync(ct);
        if (ids.Length == 0 || await _db.Sys_Menus.CountAsync(x => ids.Contains(x.MenuId) && x.Enable, ct) != ids.Length)
            throw new TenantManagementExceptionV1("TENANT_TEMPLATE_NOT_READY");
        // These revisions and all native role/menu writers already share the IAM gate.
        // The role revision catches mapping ABA; the catalog revision catches menu changes.
        var revisions = await _db.Database.SqlQuery<string>($"""
            SELECT 'default-admin-v1:' || r.revision::text || ':' || c.revision::text AS "Value"
            FROM cp6_authentication_v1.identity_admission_revisions r
            JOIN cp6_authentication_v1.identity_admission_revisions c ON c.tenant_id=r.tenant_id AND c.object_key='permission-catalog'
            WHERE r.tenant_id={source} AND r.object_key='role:1'
            """).SingleOrDefaultAsync(ct);
        return revisions == null ? throw new TenantManagementExceptionV1("TENANT_TEMPLATE_NOT_READY") : new(revisions, ids);
    }

    private Task<ManagementSaved?> CreationOriginal(string operation, CancellationToken ct)
        => _db.Database.SqlQuery<ManagementSaved>($"""
            SELECT operation_bytes AS "Operation",input_bytes AS "Input",actor_id AS "Actor",actor_tenant AS "ActorTenant",result_bytes AS "Result"
            FROM cp6_tenant_creation_v1.commands WHERE operation_hash={ManagementKey(operation)}
            """).SingleOrDefaultAsync(ct);
    private static TenantCreationResultV1 CreationReceipt(ManagementSaved saved, PlatformAdminAuthorization.Actor actor, string operation, byte[]? input)
    {
        if (saved.Actor != actor.Id || saved.ActorTenant != actor.TenantId) throw new TenantManagementExceptionV1("TENANT_ORIGINAL_FORBIDDEN", 403);
        if (!saved.Operation.AsSpan().SequenceEqual(ManagementBytes(operation)) || input != null && !saved.Input.AsSpan().SequenceEqual(input))
            throw new TenantManagementExceptionV1("TENANT_OPERATION_CONFLICT");
        return new(JsonSerializer.Deserialize<TenantCreationReceiptV1>(saved.Result)!, null, true);
    }
    public Task<TenantCreationResultV1?> ReadCreationOperationAsync(string operation, TenantManagementSessionV1 session, CancellationToken ct = default)
    {
        ManagementOperation(operation);
        return ManagementTransaction<TenantCreationResultV1?>(false, session, async (actor, token) =>
        {
            var original = await CreationOriginal(operation, token);
            return original == null ? null : CreationReceipt(original, actor, operation, null);
        }, ct);
    }
    public Task<TenantCreationResultV1> CreateVersionedAsync(TenantCreationCommandV1 command, TenantManagementSessionV1 session, CancellationToken ct = default)
    {
        ManagementOperation(command.OperationRef);
        var input = ManagementBytes(command);
        return ManagementTransaction(true, session, async (actor, token) =>
        {
            var original = await CreationOriginal(command.OperationRef, token);
            if (original != null) return CreationReceipt(original, actor, command.OperationRef, input);
            ValidateManagementConfiguration(new(command.Name, command.ExpireDate, command.Remark, null));
            ValidateCreationText(command.Code, 50); ValidateCreationText(command.AdminUserName, 100);
            if (string.IsNullOrEmpty(command.ExpectedTemplateVersion)) throw new TenantManagementExceptionV1("TENANT_TEMPLATE_VERSION_REQUIRED", 400);
            var template = await CreationTemplate(token);
            if (template.Version != command.ExpectedTemplateVersion) throw new TenantManagementExceptionV1("TENANT_TEMPLATE_CHANGED", 412);
            // Native tenant writers acquire this same IAM gate; the existing PG unique index
            // and collation remain authoritative for case-insensitive codes, including races.
            CreateTenantResult created;
            try
            {
                created = await CreateCoreAsync(command.Code, command.Name, command.ExpireDate, command.Remark,
                    command.AdminUserName, actor, token, template.MenuIds);
            }
            catch (InvalidOperationException e) when (e.Message == "E-SEC-033")
            { throw new TenantManagementExceptionV1("E-SEC-033", 409, e); }
            var adminId = await _db.Sys_Users.IgnoreQueryFilters().Where(x => x.TenantId == created.TenantId).Select(x => x.Id).SingleAsync(token);
            var at = await _db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(token);
            var receipt = new TenantCreationReceiptV1(command.OperationRef, created.TenantId, command.Code, adminId, created.AdminUserName, template, at);
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_tenant_creation_v1.commands(operation_hash,operation_bytes,input_bytes,actor_id,actor_tenant,result_bytes)
                VALUES({ManagementKey(command.OperationRef)},{ManagementBytes(command.OperationRef)},{input},{actor.Id},{actor.TenantId},{ManagementBytes(receipt)})
                """, token);
            return new TenantCreationResultV1(receipt, created.TempPassword, false);
        }, ct);
    }
    private static void ValidateCreationText(string value, int capacity)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > capacity || value.Contains('\0'))
            throw new TenantManagementExceptionV1("TENANT_CREATION_INPUT_INVALID", 400);
        try { _ = new UTF8Encoding(false, true).GetByteCount(value); }
        catch (EncoderFallbackException e) { throw new TenantManagementExceptionV1("TENANT_CREATION_INPUT_INVALID", 400, e); }
    }
}
