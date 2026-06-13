using CP6.Entity.DomainModels.Pub;

namespace CP6.Core.Services.Pub;

/// <summary>统一附件服务 —— PUB 章06 §4/§9。上传秒传 + 删除引用计数 + 草稿转正。</summary>
public interface IAttachmentService
{
    /// <summary>上传：校验大小(E-061)/类型(E-062)→MD5 秒传复用→建记录。</summary>
    Task<Pub_Attachment> UploadAsync(Stream content, string fileName, string? contentType,
        string bizType, string? bizId, string? draftToken, string? uploader);

    /// <summary>按业务取附件列表。</summary>
    Task<List<Pub_Attachment>> ListAsync(string bizType, string bizId);

    /// <summary>下载：返回元数据 + 读取流（控制器鉴权后流式输出）。</summary>
    Task<(Pub_Attachment att, Stream stream)> DownloadAsync(Guid id);

    /// <summary>删除记录；仅当无其他记录引用同 StorePath 才物理删。</summary>
    Task DeleteAsync(Guid id);

    /// <summary>草稿转正：把 draftToken 的附件回填 BizId。</summary>
    Task RebindAsync(string draftToken, string bizId);
}
