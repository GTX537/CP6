using System.Security.Cryptography;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Pub;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Pub;

/// <summary>统一附件服务实现。保留旧MD5共享路径；普通删除仅移除附件引用，不处置共享内容。</summary>
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

    public Task<Pub_Attachment?> FindAsync(Guid id) =>
        _db.Pub_Attachments.FirstOrDefaultAsync(a => a.Id == id);

    public async Task<List<Pub_Attachment>> ListDraftAsync(string draftToken) =>
        await _db.Pub_Attachments
            .Where(a => a.DraftToken == draftToken)
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
        if (_db.Database.IsNpgsql())
        {
            await UnlinkPostgreSqlAsync(id);
            return;
        }
        var att = await _db.Pub_Attachments.FindAsync(id);
        if (att == null) return;
        _db.Pub_Attachments.Remove(att);
        await _db.SaveChangesAsync();

        // A tenant-local reference count cannot authorize global physical disposal. The
        // separate store Owner must establish the complete reference/retention/hold gate.
    }

    private async Task UnlinkPostgreSqlAsync(Guid id)
    {
        var tenant = _db.CurrentTenantId;
        var transaction = _db.Database.CurrentTransaction;
        var savepoint = transaction is null ? null : "attachment_unlink_" + Guid.NewGuid().ToString("N");
        if (transaction is not null) await transaction.CreateSavepointAsync(savepoint!);
        int count;
        try
        {
            // One native statement: autocommit or the caller's transaction. Do not flush
            // other tracked business, and do not trust Find's potentially foreign local row.
            count = await _db.Pub_Attachments.Where(a => a.TenantId == tenant && a.Id == id).ExecuteDeleteAsync();
            if (transaction is not null) await transaction.ReleaseSavepointAsync(savepoint!);
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackToSavepointAsync(savepoint!);
                await transaction.ReleaseSavepointAsync(savepoint!);
            }
            throw;
        }
        if (count != 0)
            foreach (var entry in _db.ChangeTracker.Entries<Pub_Attachment>()
                         .Where(e => e.Entity.TenantId == tenant && e.Entity.Id == id).ToArray())
                entry.State = EntityState.Detached;
        // Deliberately no physical deletion, including when the last visible row is gone.
        // A later caller rollback can therefore restore the original readable reference.
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
