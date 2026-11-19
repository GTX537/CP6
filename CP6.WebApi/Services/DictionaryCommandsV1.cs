using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Services;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DictionaryCommandV1(Guid OperationRef, string? Kind, string? TypeCode, string? ExpectedVersion,
    int? Id, string? Value, string? Label, bool? Enable, int? OrderNo, string? Reason);

public sealed record DictionaryReceiptV1(Guid OperationRef, string Kind, string TypeCode, string DictionaryVersion, int Id);

public sealed class DictionaryCommandsV1(CP6Context db, DictionaryMutationOwnerV1 mutation, IamAdministrationV1 identity)
{
    private const string Owner = "Main.Sys.Dictionary.v1";
    private const string Contract = "CP6.Dictionary.v1";
    private readonly DictionaryCatalogV1 catalog = new(db);
    private readonly MainOwnerOperationJournalV1 journal = new(db);

    public async Task<object> Options(string typeCode, string? historicalValue, CancellationToken ct)
    {
        ValidateCode(typeCode);
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var dictionaryVersion = await catalog.Revision(typeCode, ct) ?? "absent";
        var types = await db.Sys_DictTypes.AsNoTracking().Where(type => type.TypeCode == typeCode).ToListAsync(ct);
        var data = await db.Sys_DictDatas.AsNoTracking().Where(item => item.TypeCode == typeCode)
            .OrderBy(item => item.OrderNo).ThenBy(item => item.Id).ToListAsync(ct);
        var type = types.Count == 1 ? types[0] : null;
        var ambiguous = types.Count > 1 || data.Select(item => item.Value).Distinct(StringComparer.Ordinal).Count() != data.Count;
        var items = data.Select(item => new
        {
            item.Id, item.Value, item.Label, item.OrderNo, item.Enable,
            Selectable = !ambiguous && type?.Enable == true && item.Enable
        }).ToArray();
        return new
        {
            dictionaryVersion, scope = "SHARED", type,
            unavailableReason = ambiguous ? "DICT_AMBIGUOUS" : type is null ? "DICT_NOT_FOUND" : !type.Enable ? "DICT_STOPPED" : null,
            items = items.Where(item => item.Selectable),
            historical = historicalValue is null || ambiguous ? null : items.SingleOrDefault(item => item.Value == historicalValue),
            allItems = items
        };
    }

    public async Task<DictionaryReceiptV1> Recover(HttpContext http, Guid operation)
        => await identity.ExecuteIdentityAsync(ct => IamAdministrationHttpV1.CurrentActor(http, http.RequestServices, ct, allowImpersonation: true),
            async (actor, ct) =>
            {
                var original = await journal.Read(Owner, operation, ct);
                if (original is null || original.Actor != actor.UserId)
                    throw Failure("DICT_ORIGINAL_NOT_FOUND", 404);
                var receipt = Receipt(original);
                if (!actor.ActionKeys.Contains("dict:" + Action(receipt.Kind)))
                    throw Failure("DICT_ORIGINAL_FORBIDDEN", 403);
                if (await IamAdministrationHttpV1.CurrentActor(http, http.RequestServices, ct, allowImpersonation: true) != actor.UserId)
                    throw Failure("REAUTH_REQUIRED", 401);
                return receipt;
            }, http.RequestAborted);

    public Task<DictionaryReceiptV1> Apply(DictionaryCommandV1 command, CancellationToken ct)
    {
        Validate(command);
        return mutation.ExecuteAsync(Action(command.Kind!), async (actor, token) =>
        {
            var bytes = MainOwnerOperationJournalV1.Encode(command);
            var original = await journal.Read(Owner, command.OperationRef, token);
            if (original is not null)
            {
                if (original.Actor != actor.UserId) throw Failure("DICT_ORIGINAL_NOT_FOUND", 404);
                if (!MainOwnerOperationJournalV1.Matches(original, bytes)) throw Failure("DICT_OPERATION_CONFLICT", 409);
                return Receipt(original);
            }

            var typeCode = command.TypeCode!;
            var revision = await catalog.Revision(typeCode, token) ?? "absent";
            if (command.Kind == "update-type")
            {
                var actual = await db.Sys_DictTypes.AsNoTracking()
                    .Where(type => type.Id == command.Id).Select(type => type.TypeCode).SingleOrDefaultAsync(token);
                if (actual is not null && actual != typeCode) throw Failure("DICT_STABLE_TYPE_CODE", 409);
            }
            if (revision != command.ExpectedVersion) throw Failure("DICT_VERSION_CHANGED", 412);
            var id = command.Kind!.EndsWith("-type", StringComparison.Ordinal)
                ? await WriteType(command, token)
                : await WriteData(command, token);
            var next = await catalog.Revision(typeCode, token);
            if (next is null || next == revision) throw Failure("DICT_VERSION_UNAVAILABLE", 503);
            var receipt = new DictionaryReceiptV1(command.OperationRef, command.Kind!, typeCode, next, id);
            await journal.Store(Owner, Contract, command.OperationRef, actor.UserId, bytes, receipt, [],
                new { command.Kind, command.Reason, TypeCode = typeCode, PreviousVersion = revision, AppliedVersion = next }, token);
            return receipt;
        }, ct);
    }

    private async Task<int> WriteType(DictionaryCommandV1 command, CancellationToken ct)
    {
        var records = await db.Sys_DictTypes.Where(type => type.TypeCode == command.TypeCode).ToListAsync(ct);
        if (records.Count > 1 || command.Kind == "create-type" && records.Count != 0)
            throw Failure("DICT_TYPE_EXISTS", 409);

        Sys_DictType current;
        if (command.Kind == "create-type")
        {
            current = new Sys_DictType { TypeCode = command.TypeCode!, CreateDate = DateTime.Now };
            db.Sys_DictTypes.Add(current);
        }
        else current = records.SingleOrDefault(type => type.Id == command.Id) ?? throw Failure("DICT_TYPE_NOT_FOUND", 404);

        if (command.Kind == "delete-type")
        {
            await catalog.RequireUnreferenced([current.Id], ct);
            if (await db.Sys_DictDatas.AnyAsync(item => item.TypeCode == current.TypeCode, ct))
                throw Failure("DICT_TYPE_HAS_ITEMS", 409);
            db.Sys_DictTypes.Remove(current);
        }
        else
        {
            current.TypeName = command.Label!;
            current.Enable = command.Enable!.Value;
            current.OrderNo = command.OrderNo!.Value;
        }
        if (!db.ChangeTracker.HasChanges()) throw Failure("DICT_NO_CHANGE", 409);
        await db.SaveChangesAsync(ct);
        return current.Id;
    }

    private async Task<int> WriteData(DictionaryCommandV1 command, CancellationToken ct)
    {
        var typeIds = await db.Sys_DictTypes.Where(type => type.TypeCode == command.TypeCode)
            .Select(type => type.Id).ToArrayAsync(ct);
        if (typeIds.Length == 0) throw Failure("DICT_TYPE_NOT_FOUND", 404);
        if (typeIds.Length > 1) throw Failure("DICT_AMBIGUOUS", 409);
        var data = await db.Sys_DictDatas.Where(item => item.TypeCode == command.TypeCode).ToListAsync(ct);
        Sys_DictData current;
        if (command.Kind == "create-data")
        {
            if (data.Any(item => item.Value == command.Value)) throw Failure("DICT_VALUE_EXISTS", 409);
            current = new Sys_DictData { TypeCode = command.TypeCode!, Value = command.Value!, CreateDate = DateTime.Now };
            db.Sys_DictDatas.Add(current);
        }
        else current = data.SingleOrDefault(item => item.Id == command.Id) ?? throw Failure("DICT_VALUE_NOT_FOUND", 404);

        if (command.Kind == "delete-data")
        {
            await catalog.RequireUnreferenced(typeIds, ct);
            db.Sys_DictDatas.Remove(current);
        }
        else
        {
            if (current.Value != command.Value) throw Failure("DICT_STABLE_VALUE", 409);
            current.Label = command.Label!;
            current.Enable = command.Enable!.Value;
            current.OrderNo = command.OrderNo!.Value;
        }
        if (!db.ChangeTracker.HasChanges()) throw Failure("DICT_NO_CHANGE", 409);
        await db.SaveChangesAsync(ct);
        return current.Id;
    }

    private static DictionaryReceiptV1 Receipt(MainOwnerOperationJournalV1.Original original)
        => JsonSerializer.Deserialize<DictionaryReceiptV1>(original.Result) ?? throw Failure("DICT_ORIGINAL_UNAVAILABLE", 503);

    private static void ValidateCode(string? typeCode)
    {
        if (string.IsNullOrWhiteSpace(typeCode) || typeCode.Length > 100 || typeCode.Any(char.IsControl))
            throw Failure("DICT_TYPE_CODE_INVALID", 400);
    }

    private static void Validate(DictionaryCommandV1 command)
    {
        ValidateCode(command.TypeCode);
        var kind = command.Kind;
        _ = Action(kind);
        var create = kind is "create-type" or "create-data";
        var delete = kind is "delete-type" or "delete-data";
        var data = kind is "create-data" or "update-data";
        if (command.OperationRef == Guid.Empty ||
            (command.ExpectedVersion != "absent" && !Guid.TryParseExact(command.ExpectedVersion, "D", out _)) ||
            (create ? command.Id is not null : command.Id is null or <= 0) ||
            string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 256 || command.Reason.Contains('\0') ||
            (delete ? command.Value is not null || command.Label is not null || command.Enable is not null || command.OrderNo is not null
                : string.IsNullOrWhiteSpace(command.Label) || command.Label.Length > (data ? 200 : 100) ||
                    command.Label.Contains('\0') || command.Enable == null || command.OrderNo == null) ||
            (data ? string.IsNullOrWhiteSpace(command.Value) || command.Value.Length > 100 || command.Value.Any(char.IsControl)
                : command.Value is not null))
            throw Failure("DICT_COMMAND_INVALID", 400);
    }

    private static string Action(string? kind) => kind switch
    {
        "create-type" or "create-data" => "add",
        "update-type" or "update-data" => "edit",
        "delete-type" or "delete-data" => "delete",
        _ => throw Failure("DICT_COMMAND_INVALID", 400)
    };

    private static OrderPriceCorrectionException Failure(string code, int status) => new(code, status);
}
