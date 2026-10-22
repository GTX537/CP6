using System.Data;
using System.Text.Json;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Erp;

public sealed partial class OrderPriceCorrectionOwnerV1
{
    private const string OperationOwner = "Main.Order.PriceCorrection";
    private const string CommandContract = "Main.Order.PriceCorrection.Command.v1";

    public Task<OrderPriceCorrectionOperationScopeV1> OperationScopeAsync(CancellationToken ct=default)
        => ReadAsync((authority,_)=>Task.FromResult(new OrderPriceCorrectionOperationScopeV1(
            authority.Source.TenantId,authority.Source.SubjectId)),ct);

    public Task<OrderPriceCorrectionBatchResultDto> ExecuteCommandAsync(OrderPriceCorrectionCommandV1 request,
        CancellationToken ct = default)
    {
        if (request == null || request.OperationId == Guid.Empty)
            throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_REQUIRED",400);
        return ExecuteCoreAsync(request,ct,request.OperationId);
    }

    private async Task<OrderPriceCorrectionBatchResultDto> DiscloseOriginal(
        MainOwnerOperationJournalV1.Original original, Guid operation, byte[]? input, CancellationToken ct)
    {
        var authority=await ReadAuthority(ct,true);
        if (original.Actor!=authority.Source.SubjectId || original.Contract!=CommandContract)
            throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_FORBIDDEN",403);
        if (original.State=="CLOSED") throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_CLOSED",409);
        if (input!=null && !MainOwnerOperationJournalV1.Matches(original,input))
            throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        if (!authority.Decision.Fields["UnitPrice"].CanRead || !authority.Decision.Fields["Amount"].CanRead)
            throw new OrderPriceCorrectionException("FIELD_READ_FORBIDDEN",403);
        var objects=await Objects(authority,original.Objects,ct);
        if (!original.Objects.All(objects.Allowed.Contains))
            throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
        var final=await ReadAuthority(ct,true);
        if (final.Source.SourcePermissionVersion!=authority.Source.SourcePermissionVersion
            || final.Source.RegistryVersion!=authority.Source.RegistryVersion || final.Scope!=authority.Scope
            || final.OrganizationVersion!=authority.OrganizationVersion || final.Source.TenantExpiresAt<=DateTime.Now
            || await currentActor(ct)!=original.Actor)
            throw new OrderPriceCorrectionException("AUTHORITY_CHANGED",403);
        var result=JsonSerializer.Deserialize<OrderPriceCorrectionBatchResultDto>(original.Result)
            ?? throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_UNAVAILABLE",503);
        if(result.OperationId!=operation)throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_UNAVAILABLE",503);
        return result;
    }

    /// <summary>Application read-only: no business, receipt, audit or authority-revision writes.
    /// Native READ COMMITTED uses the existing authority root row FOR SHARE, not PostgreSQL READ ONLY mode.</summary>
    public async Task<OrderPriceCorrectionOperationV1> ReadOperationAsync(Guid operation, CancellationToken ct=default)
        => await RecoverOperation(operation,false,ct);

    public async Task<OrderPriceCorrectionOperationV1> CloseOperationAsync(Guid operation, CancellationToken ct=default)
        => await RecoverOperation(operation,true,ct);

    private async Task<OrderPriceCorrectionOperationV1> RecoverOperation(Guid operation,bool close,CancellationToken ct)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,http.HttpContext?.RequestAborted??CancellationToken.None);
        ct=linked.Token;
        if(operation==Guid.Empty)throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_REQUIRED",400);
        if(!db.Database.IsNpgsql() || db.Database.CurrentTransaction!=null || System.Transactions.Transaction.Current!=null
            || db.ChangeTracker.HasChanges())throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted,ct);var committing=false;
        try
        {
            if(close)await AuthenticationCompletionGateV1.AcquireAsync(db,ct);
            else if(!await db.Database.SqlQueryRaw<bool>("""
                SELECT singleton AS "Value" FROM cp6_platform_admin_guard_v1.root WHERE singleton FOR SHARE
                """).SingleAsync(ct))throw new CurrentPermissionUnavailableException();
            var authority=await ReadAuthority(ct,true);
            var journal=new MainOwnerOperationJournalV1(db);var original=await journal.Read(OperationOwner,operation,ct);
            OrderPriceCorrectionOperationV1 result;
            if(original!=null)
            {
                if(original.Actor!=authority.Source.SubjectId || original.Contract!=CommandContract)
                    throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_FORBIDDEN",403);
                result=original.State=="CLOSED" ? new(operation,"CLOSED")
                    : new(operation,"COMMITTED",await DiscloseOriginal(original,operation,null,ct));
            }
            else if(close)
            {
                result=new(operation,"CLOSED");
                await journal.Store(OperationOwner,CommandContract,operation,authority.Source.SubjectId,null,result,[],
                    new { action="close-uncommitted-operation",authority.Source.SubjectId },ct);
            }
            else result=new(operation,"UNKNOWN");
            var final=await ReadAuthority(ct,true);
            if(final.Source.SourcePermissionVersion!=authority.Source.SourcePermissionVersion
                || final.Source.RegistryVersion!=authority.Source.RegistryVersion || final.Scope!=authority.Scope
                || final.OrganizationVersion!=authority.OrganizationVersion || final.Source.TenantExpiresAt<=DateTime.Now
                || await currentActor(ct)!=authority.Source.SubjectId)
                throw new OrderPriceCorrectionException("AUTHORITY_CHANGED",403);
            ct.ThrowIfCancellationRequested();committing=true;await tx.CommitAsync(ct);return result;
        }
        catch(Exception e)
        {
            try{await tx.RollbackAsync(CancellationToken.None);}catch(Exception rollback)
            {throw new OrderPriceCorrectionException("PRICE_CORRECTION_OUTCOME_UNKNOWN",503,new AggregateException(e,rollback));}
            if(close && committing)throw new OrderPriceCorrectionException("PRICE_CORRECTION_OUTCOME_UNKNOWN",503,e);
            if(e is OrderPriceCorrectionException or OperationCanceledException)throw;
            throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503,e);
        }
    }
}
