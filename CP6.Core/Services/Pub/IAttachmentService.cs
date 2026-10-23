using CP6.Entity.DomainModels.Pub;

namespace CP6.Core.Services.Pub;

/// <summary>统一附件服务。上传旧去重、移除附件引用和草稿转正；不授权共享内容的物理处置。</summary>
public interface IAttachmentService
{
    /// <summary>上传：校验大小(E-061)/类型(E-062)→MD5 秒传复用→建记录。</summary>
    Task<Pub_Attachment> UploadAsync(Stream content, string fileName, string? contentType,
        string bizType, string? bizId, string? draftToken, string? uploader);

    /// <summary>按业务取附件列表。</summary>
    Task<List<Pub_Attachment>> ListAsync(string bizType, string bizId);

    /// <summary>读取附件元数据，不打开物理文件。</summary>
    Task<Pub_Attachment?> FindAsync(Guid id);

    /// <summary>读取草稿 token 下的附件，供转正前做权限与归属校验。</summary>
    Task<List<Pub_Attachment>> ListDraftAsync(string draftToken);

    /// <summary>下载：返回元数据 + 读取流（控制器鉴权后流式输出）。</summary>
    Task<(Pub_Attachment att, Stream stream)> DownloadAsync(Guid id);

    /// <summary>仅移除附件记录，不请求物理删除。PostgreSQL已有调用方事务时，由调用方最终提交。</summary>
    Task DeleteAsync(Guid id);

    /// <summary>草稿转正：把 draftToken 的附件回填 BizId。</summary>
    Task RebindAsync(string draftToken, string bizId);
}
