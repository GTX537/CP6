using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

/// <summary>表单引擎服务（OA 章02）。表单定义 CRUD + 数据提交（服务端按 schema 复核）。</summary>
public interface IFormService
{
    /// <summary>建/改表单定义（按 FormKey upsert，schema 变更自增 Version）。返回 FormDef.Id。</summary>
    Task<Guid> SaveDefAsync(string formKey, string formName, string schemaJson, string? user = null);

    /// <summary>取表单定义（按 FormKey）。</summary>
    Task<Wf_FormDef?> GetDefAsync(string formKey);

    /// <summary>
    /// 提交表单数据：先按 schema 服务端复核 required/类型/长度，通过才落库。
    /// 校验失败抛 <see cref="InvalidOperationException"/>（携错误清单）。返回 FormData.Id。
    /// </summary>
    Task<Guid> SubmitDataAsync(string formKey, string? bizId, string dataJson, string? user = null);

    /// <summary>纯校验：返回错误清单（空=通过）。供 SubmitData 与单测复用，不触库。</summary>
    IReadOnlyList<string> ValidateData(string schemaJson, string dataJson);
}
