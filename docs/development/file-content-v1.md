# 不可变文件内容与当前授权读取 v1

W189 实际实现 `Main.File.Content.v1`，独立菜单 `/pub/file-content`、API `/api/file-content/owner-v1`。直接服务工程受控文档的真实文件前置；原 `AttachmentService` 的历史范围保持。依据 [WP02 任务卡](task-plans/WP02.md) 与 SYS-FILE-01 原七项设计，正式受控文档绑定、专业用途批准、引用图处置和工程冻结继续由对应 Owner 实施。

## 身份、存储和事务

- 文件身份、不可变内容引用、工程业务引用、检查/保留状态分别保存。内容引用含 Owner/UUID/version/SHA256/byteLength/mediaType；本版每个新 UUID 对应一个不可变内容版本 `1`，替换原件创建新 UUID，不覆盖旧字节身份。检查、隔离和保留有独立连续状态版本、ETag 和不可变历史。
- 文件名仅用于显示和下载名称。存储 key 使用服务端随机 UUID，不含用户文件名、业务编码或旧 MD5；响应不暴露路径、草稿 token、存储凭据。类型由内容特征检测，未知内容作为二进制原件；所有下载均为 attachment/octet-stream/nosniff，未实现任意主动内容 inline 预览。
- 上传元数据采用 `X-File-Command`（UTF-8 JSON 的 Base64），不进入 URL；命令对象拒绝未知字段。原始 HTTP body 在当前身份、上传动作、字段与真实工程对象权限核对后读取；一字节至 20 MiB，按声明长度加一字节探测边界，并校验实际 SHA256。Local/S3 新增有界读取能力；S3 在复制前核服务器声明长度，复制时继续核实际长度。旧读取路径不变，未提供有界能力的自定义 provider 不能进入本路径。
- 复用 `IFileStore`，先暂存和检查，再在 PostgreSQL Owner 共同事务保存内容元数据、状态、原命令回执及审计。文件存储与 PostgreSQL **没有分布式原子保证**。并发重复可能留下无引用暂存；它们不成为可访问文件，保留等待完整引用图/处置政策，不盲删或冒称已清理。已提交内容和事件由数据库不可变触发器保护；触发器未就绪时关闭采用。

## 当前权限和原操作

文件的 `query/add/edit`、`Content/FileName` 字段和数据范围共同生效。创建时核工程修订当前版本及 edit；读取时核原工程版本和当前 query，另核其产品 Owner 当前权限。部门来源是工程修订的可信映射；Scope=1 限创建者，Scope=5 仍必须满足当前父对象权限。当前用户/租户均由原会话提供。

所有新下载重新检查当前文件/父对象/产品权限与当前检查、保留状态，读取原精确版本/摘要，并在最终授权门完成后才返回字节。历史检查状态不会绕过当前 hold/隔离。摘要/长度/存储缺失导致拒绝并持久隔离，后来恢复底层字节也不会自动恢复可用；必须重新检查。已经合法下载到用户端的副本不能远程回收。

同 operation 同输入恢复原回执；同键不同输入拒绝，重复上传亦校验实际流。确认丢失先 query 原 operation；UNKNOWN 可继续查询或原子 close 未提交键。页面仅持久化 tenant/actor 命名空间内的 operation/object UUID，不保存文件正文、文件名和业务原因；跨标签页使用 Web Locks，身份变化丢弃晚到数据与下载 URL。状态变更采用 expectedVersion/ETag；HOLD/RELEASE 不修改检查结论，INSPECT 不解除 hold。

## 实际检查适配

`DefenderFileContentInspectorV1` 使用明确宿主配置：`FileContent:Defender:Enabled`、`Executable`、可选 `TemporaryDirectory`。仅 Windows 支持此适配；未配置、不可用、超时或扫描版本变化均不会产生 CLEAN。

适配器对本次随机暂存文件执行 Microsoft Defender 自定义扫描并指定 `-DisableRemediation`，规范化 Windows 路径、核前后引擎/签名版本和扫描后原字节。只有成功退出且内容未变才记录 CLEAN；非零结果保持 NOT_CLEAN，并明确可能是扫描错误，不能称为已发现恶意文件。执行超时终止本次子进程；仅删除本次随机临时文件。不会更改系统杀毒设置、排除规则或进行全盘扫描。

命令与返回值依据 [Microsoft 官方 MpCmdRun 文档](https://learn.microsoft.com/en-us/defender-endpoint/command-line-arguments-microsoft-defender-antivirus)。扫描结果证明该引擎和签名版本下的一次内容检查，不证明文件绝对无风险，也不等于技术用途批准。其他生产宿主的扫描服务注册/资格、检查更新政策由对应环境任务落实，不能把本机结果冒称生产认证。

## 尚待实际采用

本次工程上下文文件登记不是正式业务绑定。受控文档的版本/语言/转换件、独立质量规格、exact manifest/purpose grant、原 Owner 同事务绑定及完整保留/处置图仍待对应任务采用。没有物理删除、公共分享链接、预览转换或旧附件自动升级。新增迁移只在隔离 PostgreSQL 验证，未执行既有环境迁移、Actions 或部署。
