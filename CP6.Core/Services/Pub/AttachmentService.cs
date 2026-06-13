using System.Security.Cryptography;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Pub;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Pub;

/// <summary>统一附件服务实现 —— PUB 章06。MD5 秒传 + StorePath 引用计数删除。</summary>
public class AttachmentService : IAttachmentService
{
    private readonly CP6Context _db;
    private readonly IFileStore _store;
    private readonly long _maxBytes;
    private readonly HashSet<string> _allowedExt;   // 空 = 不限制

    public AttachmentService(CP6Context db, IFileStore store, int maxSizeMb = 20, IEnumerable<string>? allowedExt = null)
    {
        _db = db;
        _store = store;
        _maxBytes = maxSizeMb * 1024L * 1024L;
        _allowedExt = new HashSet<string>(
            (allowedExt ?? Enumerable.Empty<string>()).Select(x => x.TrimStart('.').ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<Pub_Attachment> UploadAsync(Stream content, string fileName, string? contentType,
        string bizType, string? bizId, string? draftToken, string? uploader)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms);
        var bytes = ms.ToArray();

        if (bytes.LongLength > _maxBytes) throw new InvalidOperationException("E-PUB-061");   // 超大
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        if (_allowedExt.Count > 0 && !_allowedExt.Contains(ext)) throw new InvalidOperationException("E-PUB-062");   // 类型不许

        var hash = Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
        var existing = await _db.Pub_Attachments.FirstOrDefaultAsync(a => a.FileHash == hash);

        string storeName, storePath;
        if (existing != null)   // 秒传：复用既有物理文件
        {
            storeName = existing.StoreName;
            storePath = existing.StorePath;
        }
        else
        {
            storeName = $"{hash[..2]}/{hash[2..4]}/{hash}{(ext.Length > 0 ? "." + ext : "")}";
            storePath = await _store.SaveAsync(new MemoryStream(bytes), storeName);
        }

        var att = new Pub_Attachment
        {
            Id = Guid.NewGuid(),
            BizType = bizType,
            BizId = bizId,
            DraftToken = draftToken,
            FileName = fileName,
            StoreName = storeName,
            StorePath = storePath,
            Size = bytes.LongLength,
            ContentType = contentType,
            FileHash = hash,
            Uploader = uploader,
            CreateDate = DateTime.Now
        };
        _db.Pub_Attachments.Add(att);
        await _db.SaveChangesAsync();
        return att;
    }

    public async Task<List<Pub_Attachment>> ListAsync(string bizType, string bizId) =>
        await _db.Pub_Attachments
            .Where(a => a.BizType == bizType && a.BizId == bizId)
            .OrderBy(a => a.CreateDate)
            .ToListAsync();

    public async Task<(Pub_Attachment att, Stream stream)> DownloadAsync(Guid id)
    {
        var att = await _db.Pub_Attachments.FindAsync(id) ?? throw new InvalidOperationException("E-PUB-404");
        var stream = await _store.OpenReadAsync(att.StorePath);
        return (att, stream);
    }

    public async Task DeleteAsync(Guid id)
    {
        var att = await _db.Pub_Attachments.FindAsync(id);
        if (att == null) return;
        var storePath = att.StorePath;
        _db.Pub_Attachments.Remove(att);
        await _db.SaveChangesAsync();

        // 引用计数：还有其他记录指向同 StorePath 则保留物理文件
        var stillReferenced = await _db.Pub_Attachments.AnyAsync(a => a.StorePath == storePath);
        if (!stillReferenced) await _store.DeleteAsync(storePath);
    }

    public async Task RebindAsync(string draftToken, string bizId)
    {
        var rows = await _db.Pub_Attachments.Where(a => a.DraftToken == draftToken).ToListAsync();
        foreach (var r in rows)
        {
            r.BizId = bizId;
            r.DraftToken = null;
        }
        await _db.SaveChangesAsync();
    }
}
