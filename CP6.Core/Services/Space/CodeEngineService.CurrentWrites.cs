using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Space;

public partial class CodeEngineService
{
    private readonly CP6Context _db;
    private readonly IamAdministrationV1? _administration;
    private readonly TenantManagementSessionV1? _session;
    private readonly CancellationToken _requestCancellation;

    public CodeEngineService(CP6Context db) : this(db, null, null, default) { }

    public CodeEngineService(CP6Context db, IamAdministrationV1? administration,
        TenantManagementSessionV1? session, CancellationToken requestCancellation)
    {
        _db = db;
        _administration = administration;
        _session = session;
        _requestCancellation = requestCancellation;
    }

    /// <inheritdoc/>
    public Task<Guid> CreateRuleAsync(CodeRuleDto d, string? user)
        => ExecuteMutationAsync("add", false, token => CreateRuleCoreAsync(d, user, token));

    /// <inheritdoc/>
    public Task UpdateRuleAsync(Guid id, CodeRuleDto d, string? user)
        => ExecuteMutationAsync("edit", false, async token =>
        {
            await UpdateRuleCoreAsync(id, d, user, token);
            return true;
        });

    /// <inheritdoc/>
    public Task DeleteRuleAsync(Guid id)
        => ExecuteMutationAsync("delete", false, async token =>
        {
            await DeleteRuleCoreAsync(id, token);
            return true;
        });

    /// <inheritdoc/>
    public Task<List<string>> GenerateAsync(Guid floorId, string mode, Guid? scopeZoneId)
        => ExecuteMutationAsync("generate", true, token => GenerateCoreAsync(floorId, mode, scopeZoneId, token));

    /// <inheritdoc/>
    public Task<string> GenSingleAsync(Guid locationId)
        => ExecuteMutationAsync("generate", true, token => GenSingleCoreAsync(locationId, token));

    private async Task<T> ExecuteMutationAsync<T>(string action, bool generation, Func<CancellationToken, Task<T>> body)
    {
        if (!_db.Database.IsNpgsql()) return await body(CancellationToken.None);
        if (_administration?.Owns(_db) != true || _session == null)
            throw new OrderPriceCorrectionException("SPACE_CODE_AUTHORITY_UNAVAILABLE", 503);
        try
        {
            return await _administration.ExecuteCurrentAsync("space-code-rule:" + action, _session.CurrentActor,
                async (actor, token) =>
                {
                    if (generation)
                    {
                        await _db.Database.ExecuteSqlRawAsync("""
                            LOCK TABLE public."Space_CodeRule", public."Space_Site", public."Space_Floor",
                                public."Space_Zone", public."Space_Aisle", public."Space_Rack" IN SHARE MODE
                            """, token);
                        await _db.Database.ExecuteSqlRawAsync("""
                            LOCK TABLE public."Space_Location" IN SHARE ROW EXCLUSIVE MODE
                            """, token);
                    }
                    else
                    {
                        await _db.Database.ExecuteSqlRawAsync("""
                            LOCK TABLE public."Space_CodeRule" IN SHARE ROW EXCLUSIVE MODE
                            """, token);
                    }
                    var result = await body(token);
                    if (await _session.CurrentActor(token) != actor.UserId)
                        throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401);
                    return result;
                }, _requestCancellation);
        }
        catch (IamAdministrationExceptionV1 error)
        {
            throw new OrderPriceCorrectionException(error.Message == "IAM_OPERATION_UNKNOWN"
                ? "SPACE_CODE_OUTCOME_UNKNOWN" : error.Message, error.Status, error);
        }
        catch (Exception error) when (error is DbException or DbUpdateException)
        { throw new OrderPriceCorrectionException("SPACE_CODE_AUTHORITY_UNAVAILABLE", 503, error); }
    }
}
