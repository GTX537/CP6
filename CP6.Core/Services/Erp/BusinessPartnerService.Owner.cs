using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Erp;

public partial class BusinessPartnerService
{
    private const string Resource = OperationsOwnerV1.BusinessPartner;
    private const string ProfileQuery = OperationsOwnerV1.BusinessPartnerProfileQuery;
    private OperationsOwnerV1 Owner => _owner?.Owns(_db) == true
        ? _owner
        : throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);

    private IQueryable<BusinessPartner> LegacyRows(bool includeDeleted = false)
        => _db.BusinessPartners.AsNoTracking().Where(x => includeDeleted || !x.IsDeleted);

    // 旧 DTO 无法表达逐字段及法人遮罩，正式工作区记录从新读协议返回。
    private Task<IQueryable<BusinessPartner>> LegacyProjection(OperationsAccessV1 access, CancellationToken ct, bool includeDeleted = false)
    {
        var rows = access.BusinessPartners(includeDeleted);
        return _workspace?.LegacyProjection(rows, ct) ?? Task.FromResult(rows);
    }

    public Task<BusinessPartnerDto?> GetByCdAsync(string bpCd, bool includeDeleted = false)
        => _db.Database.IsNpgsql()
            ? Owner.ExecuteAsync(Resource, ProfileQuery, false,
                async (access, ct) => await ReadAsync(await LegacyProjection(access, ct, includeDeleted), bpCd, access.BusinessPartnerNameReadable, ct))
            : ReadAsync(LegacyRows(includeDeleted), bpCd, true, default);

    public Task<bool> ExistsAsync(string bpCd)
        => _db.Database.IsNpgsql()
            ? Owner.ExecuteAsync(Resource, "query", false,
                async (access, ct) => await (await LegacyProjection(access, ct)).AnyAsync(row => row.BpCd == bpCd, ct))
            : LegacyRows().AnyAsync(row => row.BpCd == bpCd);

    public Task<(List<BpListItemDto> Rows, int Total)> SearchAsync(BpQueryDto query)
    {
        if (!_db.Database.IsNpgsql()) return SearchCoreAsync(LegacyRows(), query, true, default);
        var input = OperationsOwnerV1.Freeze(query);
        return Owner.ExecuteAsync(Resource, ProfileQuery, false,
            async (access, ct) => await SearchCoreAsync(await LegacyProjection(access, ct), input, access.BusinessPartnerNameReadable, ct));
    }

    public async Task<byte[]> ExportListCsvAsync(BpQueryDto query)
    {
        if (!_db.Database.IsNpgsql())
        {
            var legacy = await SearchCoreAsync(LegacyRows(), query, true, default, export: true);
            return EncodeCsv(legacy.Item1);
        }

        var input = OperationsOwnerV1.Freeze(query);
        return await Owner.ExecuteAsync(Resource, "export", false, async (access, ct) =>
        {
            var (rows, _) = await SearchCoreAsync(await LegacyProjection(access, ct), input, access.BusinessPartnerNameExportable, ct, export: true);
            return EncodeCsv(rows);
        });
    }

    public async Task<BusinessPartnerDto?> CreateAsync(BusinessPartnerDto dto, string? userName, bool preRegister = false)
    {
        if (!_db.Database.IsNpgsql())
        {
            var saved = await CreateCoreAsync(dto, userName, preRegister, reserveDeletedCode: false, default);
            return await GetByCdAsync(saved.BpCd);
        }

        var input = OperationsOwnerV1.Freeze(dto);
        return await Owner.ExecuteAsync(Resource, "add", true, async (access, ct) =>
        {
            access.RequireBusinessPartnerCreation();
            if (!access.BusinessPartnerNameWritable) throw new OrderPriceCorrectionException("FIELD_WRITE_FORBIDDEN", 403);
            var query = await access.BusinessPartnerReadbackAsync(ct);
            if (_workspace is not null) await _workspace.RequireLegacyCreateAsync(input.BaseCd, ct);
            var saved = await CreateCoreAsync(input, access.Actor, preRegister, reserveDeletedCode: true, ct);
            return query == null ? null : ToDto(saved, query.BusinessPartnerNameReadable);
        });
    }

    public async Task<BusinessPartnerDto?> UpdateAsync(string bpCd, BusinessPartnerDto dto, string? userName)
    {
        if (!_db.Database.IsNpgsql())
        {
            await UpdateCoreAsync(await LegacyWriteRowAsync(bpCd), dto, userName, default);
            return await GetByCdAsync(bpCd);
        }

        var input = OperationsOwnerV1.Freeze(dto);
        return await Owner.ExecuteAsync(Resource, "edit", true, async (access, ct) =>
        {
            var row = await WriteRowAsync(access, bpCd, ct);
            if (_workspace is not null) await _workspace.RequireLegacyEditAsync(row, input.BaseCd, ct);
            if (!string.Equals(input.BpCd, row.BpCd, StringComparison.Ordinal))
                throw new OrderPriceCorrectionException("BUSINESS_PARTNER_CODE_IMMUTABLE", 400);
            var query = await access.BusinessPartnerReadbackAsync(ct);
            if (input.BpName == null)
                input.BpName = row.BpName;
            else if (!access.BusinessPartnerNameWritable &&
                (query?.BusinessPartnerNameReadable != true || !string.Equals(input.BpName, row.BpName, StringComparison.Ordinal)))
                throw new OrderPriceCorrectionException("FIELD_WRITE_FORBIDDEN", 403);

            await UpdateCoreAsync(row, input, access.Actor, ct);
            // 同一资源的写入与回读共用已授权对象，更新不会改变对象标识或归属。
            return query == null ? null : ToDto(row, query.BusinessPartnerNameReadable);
        });
    }

    public async Task DeleteAsync(string bpCd, byte[]? rowVersion, string? userName)
    {
        if (!_db.Database.IsNpgsql())
        {
            await DeleteCoreAsync(await LegacyWriteRowAsync(bpCd), rowVersion, userName, default);
            return;
        }

        var version = rowVersion?.ToArray();
        await Owner.ExecuteAsync(Resource, "del", true, async (access, ct) =>
        {
            access.RequireBusinessPartnerAdministrator();
            var row = await WriteRowAsync(access, bpCd, ct);
            if (_workspace is null || !await _workspace.RetireCompatibilityAsync(access, row, version, ct))
                await DeleteCoreAsync(row, version, access.Actor, ct);
            return true;
        });
    }

    private async Task<BusinessPartner> LegacyWriteRowAsync(string bpCd)
        => await LegacyRows().AsTracking().FirstOrDefaultAsync(x => x.BpCd == bpCd)
            ?? throw new KeyNotFoundException("未找到该业务伙伴。");

    private static async Task<BusinessPartner> WriteRowAsync(OperationsAccessV1 access, string bpCd, CancellationToken ct)
        => await access.BusinessPartners().AsTracking().FirstOrDefaultAsync(x => x.BpCd == bpCd, ct)
            ?? throw new OrderPriceCorrectionException("BUSINESS_PARTNER_NOT_FOUND", 404);
}
