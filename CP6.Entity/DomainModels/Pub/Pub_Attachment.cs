using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Pub;

/// <summary>
/// 统一附件 —— PUB 章06。挂在任意业务单据上（BizType + BizId）。
/// MD5 秒传：相同内容多条记录共享同一 StorePath；删除按 StorePath 引用计数决定是否物理删。
/// 草稿期 BizId 未定时用 DraftToken 暂存，单据保存后 RebindAsync 回填 BizId。
/// </summary>
[Table("Pub_Attachment")]
public class Pub_Attachment : BaseEntity
{
    /// <summary>业务类型（如 order / purchase），= 数据/操作权限资源键</summary>
    [MaxLength(50)]
    public string BizType { get; set; } = "";

    /// <summary>业务单据 Id（Guid 串或业务编号；草稿期为空）</summary>
    [MaxLength(100)]
    public string? BizId { get; set; }

    /// <summary>草稿 token（BizId 未定时暂存，保存后回填 BizId）</summary>
    [MaxLength(64)]
    public string? DraftToken { get; set; }

    /// <summary>原始文件名</summary>
    [MaxLength(260)]
    public string FileName { get; set; } = "";

    /// <summary>存储文件名（hash+扩展名）</summary>
    [MaxLength(100)]
    public string StoreName { get; set; } = "";

    /// <summary>存储相对路径（IFileStore 内）</summary>
    [MaxLength(300)]
    public string StorePath { get; set; } = "";

    /// <summary>字节大小</summary>
    public long Size { get; set; }

    /// <summary>MIME 类型</summary>
    [MaxLength(120)]
    public string? ContentType { get; set; }

    /// <summary>内容 MD5（秒传 + 引用计数键）</summary>
    [MaxLength(32)]
    public string FileHash { get; set; } = "";

    /// <summary>上传人</summary>
    [MaxLength(100)]
    public string? Uploader { get; set; }
}
