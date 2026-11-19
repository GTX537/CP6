using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

/// <summary>共享字典的当前可选值和跨组织保留引用。</summary>
internal sealed class DictionaryCatalogV1(CP6Context db)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    internal Task<string?> Revision(string typeCode, CancellationToken ct) => db.Database.SqlQuery<string>($"""
        SELECT revision::text AS "Value" FROM cp6_dict_v1.revisions WHERE type_code = rtrim({typeCode}, ' ')
        """).SingleOrDefaultAsync(ct);

    internal IQueryable<Sys_DictData> Active(string typeCode) =>
        db.Sys_DictDatas.AsNoTracking().Where(item => item.TypeCode == typeCode && item.Enable &&
            db.Sys_DictTypes.Any(type => type.TypeCode == item.TypeCode && type.Enable));

    internal async Task RequireUnreferenced(int[] typeIds, CancellationToken ct)
    {
        if (typeIds.Length == 0) return;
        if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_form_options_v1.read_source() AS \"Value\"").SingleAsync(ct))
            throw new OrderPriceCorrectionException("DICT_REFERENCE_COVERAGE_UNAVAILABLE", 503);

        // 字典在各组织共享，含保留版本的任一表单引用都会阻止永久删除。
        var schemas = await db.Database.SqlQuery<string>($"""
            SELECT source AS "Value"
            FROM (
                SELECT schema_json AS source FROM cp6_form_authoring_v2.drafts
                UNION ALL SELECT snapshot_json::jsonb->'fields'->>'SchemaJson' FROM cp6_form_authoring_v2.versions
                UNION ALL SELECT "SchemaJson" FROM public."Wf_FormDef"
                UNION ALL SELECT "SchemaJson" FROM public."Wf_FormDefVersion"
            ) sources
            WHERE strpos(lower(source), 'optionsourcev1') > 0
            """).ToListAsync(ct);
        var ids = typeIds.ToHashSet();
        foreach (var source in schemas)
        {
            try
            {
                var schema = JsonSerializer.Deserialize<FormSchema>(source, Web);
                if (schema?.Fields == null) throw new JsonException("表单引用格式无效。");
                if (SharedDictionaryOptionsV1.Fields(schema).Any(field =>
                        field.Source.Owner == FormOptionSourceV1.OwnerName && ids.Contains(field.Source.TypeId)))
                    throw new OrderPriceCorrectionException("DICT_REFERENCED_STOP_INSTEAD", 409);
            }
            catch (JsonException error)
            { throw new OrderPriceCorrectionException("DICT_REFERENCE_COVERAGE_UNAVAILABLE", 503, error); }
        }
    }
}
