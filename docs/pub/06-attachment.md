# PUB 06 · 附件 / 文件统一管理 详细需求规格

*--- 可直接用于编写代码的最终版本 ---*

| 属性 | 内容 |
|---|---|
| 章节ID | PUB-06 附件/文件统一管理 |
| 所属模块 | PUB 公共平台 · Part 2 公共模组 |
| 里程碑 | **M4** |
| 技术栈 | Vue3 + Element Plus（上传组件）/ .NET8 Web API / SQL Server + 文件存储（本地/OSS/MinIO） |
| 命名空间 | **`Pub`**（新建：`DomainModels/Pub`、`Services/Pub`） |
| 性质 | **新建**（CP6 无统一附件能力） |

> **题眼**：CP6 各模块要存附件（合同、图纸、凭证扫描件）时各存各的、散乱无章。本章新建**统一附件服务**：任意业务单据通过 `BizType + BizId` 挂附件，物理存储走**可切换的存储抽象** `IFileStore`（本地/OSS/MinIO，商用多客户按需配），上传校验 + MD5 秒传去重，下载受业务单据的数据权限约束。**一套附件服务，所有模块复用。**

---

## 目录
- 第1章 概述（各存各的 → 统一附件）
- 第2章 数据模型（Pub_Attachment）
- 第3章 存储抽象 IFileStore（可切换）
- 第4章 上传（校验 / 秒传 / 落库）
- 第5章 下载 / 删除 / 列表（鉴权）
- 第6章 业务挂接（BizType + BizId）
- 第7章 前端通用上传组件
- 第8章 字段明细 / 控制矩阵
- 第9章 处理详细
- 第10章 API 接口设计
- 第11章 消息一览
- 第12章 集成与依赖

---

## 第1章 概述

| 维度 | 现状 | 升级后 |
|---|---|---|
| 附件存储 | 各模块各自处理、散乱 | 统一 `Pub_Attachment` + `IFileStore` |
| 存储介质 | 无约定 | 可切换：本地 / OSS / MinIO（配置） |
| 挂接 | 无统一锚 | `BizType + BizId` 挂任意单据 |
| 去重 | 无 | MD5 秒传（同文件不重复存物理） |
| 鉴权 | 无 | 下载受业务单据数据权限约束 |

**范围**：附件模型 + 存储抽象 + 上传/下载/删除/列表 + 前端通用上传组件 + 业务挂接。

---

## 第2章 数据模型（Pub_Attachment）

```csharp
// CP6.Entity/DomainModels/Pub/Pub_Attachment.cs（新建）
[Table("Pub_Attachment")]
public class Pub_Attachment : BaseEntity     // 含 Id/TenantId/CreateTime
{
    public string  BizType    { get; set; } = "";  // 业务类型，如 order / po / contract
    public string  BizId      { get; set; } = "";  // 业务单据 Id（字符串兼容各种主键）
    public string  FileName   { get; set; } = "";  // 原始文件名（展示用）
    public string  StoreName  { get; set; } = "";  // 存储文件名（Guid+扩展名，防重名）
    public string  StorePath  { get; set; } = "";  // 存储相对路径 / 对象 key
    public long    Size       { get; set; }         // 字节
    public string? ContentType{ get; set; }         // MIME，如 image/png
    public string? FileHash   { get; set; }         // MD5，秒传去重
    public string? Uploader   { get; set; }
}
```
```sql
CREATE INDEX IX_Pub_Attachment_Biz  ON Pub_Attachment(TenantId, BizType, BizId);  -- 按单据列附件
CREATE INDEX IX_Pub_Attachment_Hash ON Pub_Attachment(TenantId, FileHash);        -- 秒传查重
```

> **`BizType + BizId` 是挂接锚**：附件不属于附件自己，它挂在某张业务单据上。一张采购订单的所有附件 = `where BizType='po' and BizId=该PO的Id`。

---

## 第3章 存储抽象 IFileStore（可切换）

```csharp
// CP6.Core/Services/Pub/IFileStore.cs —— 物理存储抽象，介质可切换
public interface IFileStore
{
    Task<string> SaveAsync(Stream content, string storeName);  // 返回 StorePath / 对象 key
    Task<Stream> ReadAsync(string storePath);
    Task DeleteAsync(string storePath);
}
// 实现（按配置注入其一）：
//   LocalFileStore  —— 落本地磁盘（开发/单机）
//   OssFileStore    —— 阿里云 OSS / S3
//   MinioFileStore  —— 自建对象存储
services.AddScoped<IFileStore, LocalFileStore>();   // appsettings: Storage:Provider 决定
```

> **商用多客户必须存储可切换**：单机客户用本地盘，云客户用 OSS/MinIO。把物理存储抽象成 `IFileStore`，附件业务逻辑（`Pub_Attachment`）与介质解耦——换存储只改注入，附件表与上传/下载逻辑不动。

---

## 第4章 上传（校验 / 秒传 / 落库）

```csharp
// CP6.Core/Services/Pub/AttachmentService.cs
public async Task<Pub_Attachment> UploadAsync(IFormFile file, string bizType, string bizId, string? user)
{
    GuardSize(file.Length);                         // 大小上限（配置，超 → E-PUB-061）
    GuardType(file.FileName);                        // 扩展名/MIME 白名单（违规 → E-PUB-062）

    var hash = await Md5Async(file);                 // 秒传：同租户同 hash 已存物理文件则复用 StorePath
    var existing = await _db.Pub_Attachments.FirstOrDefaultAsync(a => a.FileHash == hash);
    var storePath = existing?.StorePath
        ?? await _store.SaveAsync(file.OpenReadStream(), $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}");

    var att = new Pub_Attachment {
        BizType = bizType, BizId = bizId, FileName = file.FileName,
        StoreName = Path.GetFileName(storePath), StorePath = storePath,
        Size = file.Length, ContentType = file.ContentType, FileHash = hash, Uploader = user };
    _db.Pub_Attachments.Add(att);
    await _db.SaveChangesAsync();
    return att;
}
```

> **秒传 = 物理去重**：同一文件（MD5 相同）被多张单据上传时，物理只存一份，附件记录各建一条指向同一 `StorePath`。删除时要判断该 `StorePath` 是否还被其他记录引用，最后一个引用删除才删物理文件（引用计数，见第9章）。

---

## 第5章 下载 / 删除 / 列表（鉴权）

```
列表：GET 按 BizType+BizId 返回附件列表（FileName/Size/Uploader/UploadTime）
下载：① 鉴权——当前用户能看该业务单据吗？（受章02/03 权限约束）
      ② IFileStore.ReadAsync(StorePath) 流式返回，响应头带原始 FileName
删除：① 鉴权 ② 删 Pub_Attachment 记录 ③ 引用计数=0 才 IFileStore.DeleteAsync 物理删
```

> **下载必须鉴权**：附件挂在业务单据上，能不能下载取决于"能不能看这张单据"——直接拿 attachmentId 下载也要回查业务单据的数据权限，不能裸下。否则数据权限（章03）在附件这里被旁路。

---

## 第6章 业务挂接（BizType + BizId）

业务单据保存后，前端用 `BizType + BizId` 调附件接口挂接：
```
采购订单页 → <PubUpload bizType="po" :bizId="poId" />
合同管理 → <PubUpload bizType="contract" :bizId="contractId" />
```
- `BizType` 约定为业务资源键（与字典/权限的 resourceKey 体系一致，便于鉴权回查）。
- 新建单据时 `BizId` 未定 → 先暂存（临时 token），保存单据后回填 `BizId`（草稿附件转正）。

---

## 第7章 前端通用上传组件

```
<PubUpload bizType bizId :maxSize :accept :multiple />
  - 拖拽 / 点击上传，进度条
  - 上传后刷新附件列表
  - 列表：文件名 | 大小 | 上传人 | 时间 | [下载][预览][删除]
  - 图片类支持缩略图 / 预览；其他类型按扩展名图标
```

---

## 第8章 字段明细 / 控制矩阵

| 字段 | 控件 | 说明 |
|---|---|---|
| file | 上传控件 | 拖拽/选择；受 maxSize/accept 限制 |
| bizType/bizId | (组件 props) | 挂接锚，不由用户填 |
| 附件列表 | 表格(只读) | 文件名/大小/上传人/时间 + 操作 |

**控制矩阵**：无权限看业务单据 → 上传/下载/删除按钮禁用（受章02 操作权限 + 章03 数据权限）。

---

## 第9章 处理详细

### 9.1 上传
```
校验大小(E-PUB-061)/类型(E-PUB-062) → 算MD5 → 秒传查重(命中复用StorePath,否则 IFileStore.SaveAsync) → 建 Pub_Attachment(挂BizType+BizId)
```

### 9.2 下载
```
鉴权(能看该单据?) → IFileStore.ReadAsync → 流式返回(Content-Disposition 带原 FileName)
```

### 9.3 删除（引用计数）
```
鉴权 → 删 Pub_Attachment 记录 → count(StorePath 被引用)==0 ? IFileStore.DeleteAsync 物理删 : 保留物理
```

### 9.4 草稿附件转正
```
新建单据未保存 → 附件挂临时 token → 单据保存得到 BizId → 回填 Pub_Attachment.BizId（token→bizId）
```

---

## 第10章 API 接口设计（.NET8）

前缀 `/api/pub/attachment`：

| 端点 | 方法 | 说明 |
|---|---|---|
| `/upload` | POST | multipart 上传（bizType/bizId + file），校验+秒传+落库 |
| `/list` | GET | 按 bizType+bizId 列附件 |
| `/{id}/download` | GET | 下载（鉴权 + 流式） |
| `/{id}/preview` | GET | 预览（图片/PDF，鉴权） |
| `/{id}` | DELETE | 删除（鉴权 + 引用计数物理删） |

---

## 第11章 消息一览

| ID | 种别 | 内容 | 触发 |
|---|---|---|---|
| E-PUB-061 | Error | 文件大小超过上限（{n}MB） | 超配置上限 |
| E-PUB-062 | Error | 不支持的文件类型 | 扩展名/MIME 不在白名单 |
| E-PUB-063 | Error | 无权限访问该附件 | 鉴权回查业务单据失败 |

---

## 第12章 集成与依赖

| 关系 | 说明 |
|---|---|
| → 全业务模块 | 任意单据 `<PubUpload bizType bizId>` 挂附件 |
| ← 章02/03 权限 | 上传/下载/删除受操作权限 + 数据权限约束（能看单据才能动附件） |
| ← 章05 日志 | 上传/删除可记操作日志 |
| 存储 | `IFileStore` 可切换（本地/OSS/MinIO），商用多客户按需配 |
| 多租户 | `Pub_Attachment` 带 TenantId；存储路径按租户隔离 |

> 附件是典型的"被所有模块复用的公共基建"：业务模块不关心文件怎么存、存哪，只管 `bizType+bizId` 挂接。存储介质、去重、鉴权全在 PUB 统一处理。

---

## 自检
- [ ] 附件靠什么挂到业务单据上？为什么用 BizType+BizId 而非外键？
- [ ] 为什么存储要抽象成 IFileStore？商用多客户的意义？
- [ ] 秒传怎么实现？删除时怎么避免误删被复用的物理文件？
- [ ] 下载为什么必须鉴权？不鉴权会旁路哪个权限？
- [ ] 新建单据时 BizId 还没有，附件怎么挂？

---

*实现：新建 `CP6.Entity/DomainModels/Pub/Pub_Attachment.cs` + `CP6.Core/Services/Pub/{AttachmentService,IFileStore,LocalFileStore}.cs` + 前端 `PubUpload` 组件；下载鉴权复用章02/03 权限。配套 xlsx 详细设计见同名 `.xlsx`。*
