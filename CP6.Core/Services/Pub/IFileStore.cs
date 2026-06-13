namespace CP6.Core.Services.Pub;

/// <summary>
/// 文件存储抽象 —— PUB 章06 §3。本地/OSS/MinIO 可切换（按 Storage:Provider 注入）。
/// 仅管字节落地/读取/删除；业务元数据在 Pub_Attachment。
/// </summary>
public interface IFileStore
{
    /// <summary>保存内容到 storeName（相对路径），返回最终相对路径。</summary>
    Task<string> SaveAsync(Stream content, string storeName);

    /// <summary>打开读取流（不存在抛 FileNotFoundException）。</summary>
    Task<Stream> OpenReadAsync(string storePath);

    /// <summary>物理删除（不存在则忽略）。</summary>
    Task DeleteAsync(string storePath);

    /// <summary>是否存在。</summary>
    bool Exists(string storePath);
}
