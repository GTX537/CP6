using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

/// <summary>表单引擎服务（OA 章02）。表单定义 CRUD + 数据提交（服务端按 schema 复核）。</summary>
public interface IFormService
{
    /// <summary>建/改表单定义（按 FormKey upsert，schema 变更自增 Version）。返回 FormDef.Id。</summary>
    Task<Guid> SaveDefAsync(string formKey, string formName, string schemaJson, string? user = null);

    Task<DefinitionDraftDto?> GetDraftAsync(string formKey, bool createIfMissing = true, string? user = null);
    Task<DefinitionDraftDto> SaveDraftAsync(string formKey, string formName, string schemaJson, byte[]? rowVersion, string? user = null);
    Task<DefinitionPublishResult> PublishAsync(string formKey, byte[]? rowVersion, Guid publishedBy, CancellationToken ct = default);
    Task<IReadOnlyList<DefinitionVersionItem>> ListVersionsAsync(string formKey, CancellationToken ct = default);
    Task<DefinitionVersionDto?> GetVersionAsync(string formKey, int version, CancellationToken ct = default);

    /// <summary>取表单定义（按 FormKey）。</summary>
    Task<Wf_FormDef?> GetDefAsync(string formKey);

    /// <summary>
    /// 提交表单数据：先按 schema 服务端复核 required/类型/长度，通过才落库。
    /// 校验失败抛 <see cref="InvalidOperationException"/>（携错误清单）。返回 FormData.Id。
    /// </summary>
    Task<Guid> SubmitDataAsync(string formKey, string? bizId, string dataJson, string? user = null);

    /// <summary>纯校验：返回错误清单（空=通过）。供 SubmitData 与单测复用，不触库。</summary>
    IReadOnlyList<string> ValidateData(string schemaJson, string dataJson);

    /// <summary>
    /// 服务端规则复算 + 复核（章06 §6，与前端 ruleEngine 同语义）：按 schema.Rules 重算 compute、
    /// 按生效 required/可见复核。返回 (复算后的 dataJson, 错误清单)。不触库，供 SubmitData 与单测复用。
    /// </summary>
    (string dataJson, IReadOnlyList<string> errors) RecomputeAndValidate(string schemaJson, string dataJson);
}
