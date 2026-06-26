/* ============================================================================
 * import-langs.sql — 把仓库里的词条 JSON 幂等导入 Sys_Lang（upsert）
 * ----------------------------------------------------------------------------
 * 用途：在新环境 / 干净环境把 deploy/seed-data/sys_lang.json 灌回 Sys_Lang。
 *       按自然键 (LangKey, TenantId) upsert —— 不重复、不误覆盖、可反复跑。
 *       与 export-langs.sql 成对（详见 deploy/runbook.md §5）。
 *
 * 前置：把 sys_lang.json 拷进容器，路径与下方 @path 一致：
 *   docker cp deploy/seed-data/sys_lang.json cp6-db:/var/opt/mssql/backup/sys_lang.json
 *
 * 运行：
 *   docker exec cp6-db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C \
 *     -i /var/opt/mssql/backup/import-langs.sql
 *
 * 注意：OPENROWSET(BULK) 需 ADMINISTER BULK OPERATIONS 权限（sa 自带）。
 *       不导入 Id（identity 自增）/ UpdatedBy / UpdatedAt（审计列），只搬词条内容 + Status。
 *
 * ⚠️ 编码：sys_lang.json 是 UTF-8；但 CP6DB collation = Chinese_PRC_CI_AS（非 _UTF8），
 *    SINGLE_CLOB 会按库代码页(GBK)读 → 直接读 UTF-8 文件会损坏日/韩及部分字符。
 *    真要 SQL 直灌时，二选一：
 *      (a) 目标库使用 _UTF8 collation（如 Chinese_PRC_CI_AS_SC_UTF8）；或
 *      (b) 把 sys_lang.json 转存 UTF-16LE，并把下方 SINGLE_CLOB 改为 SINGLE_NCLOB。
 *    （纯做版本控制/灾备快照，sys_lang.json 本身即可，无需关心此项。
 *      更稳的长期方案 = 写个 C# IDataSeeder 读 JSON 用 EF upsert，.NET 原生 UTF-8 无此坑。）
 * ========================================================================== */

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @path nvarchar(400) = N'/var/opt/mssql/backup/sys_lang.json';
DECLARE @json nvarchar(max);

SELECT @json = BulkColumn
FROM OPENROWSET(BULK '/var/opt/mssql/backup/sys_lang.json', SINGLE_CLOB) AS src;

IF @json IS NULL
BEGIN
    RAISERROR('未读到 JSON：确认 %s 已拷入容器', 16, 1, @path);
    RETURN;
END

BEGIN TRAN;

MERGE dbo.Sys_Langs AS t
USING (
    SELECT LangKey, TenantId, Status, ZhCN, ZhTW, En, Ja, Ko
    FROM OPENJSON(@json) WITH (
        LangKey  nvarchar(200) '$.LangKey',
        TenantId int           '$.TenantId',
        Status   nvarchar(20)  '$.Status',
        ZhCN     nvarchar(500) '$.ZhCN',
        ZhTW     nvarchar(500) '$.ZhTW',
        En       nvarchar(500) '$.En',
        Ja       nvarchar(500) '$.Ja',
        Ko       nvarchar(500) '$.Ko'
    )
) AS s
ON  t.LangKey = s.LangKey
AND (t.TenantId = s.TenantId OR (t.TenantId IS NULL AND s.TenantId IS NULL))  -- null 全局键匹配
WHEN MATCHED THEN UPDATE SET
    t.Status = ISNULL(s.Status, t.Status),
    t.ZhCN = s.ZhCN, t.ZhTW = s.ZhTW, t.En = s.En, t.Ja = s.Ja, t.Ko = s.Ko
WHEN NOT MATCHED BY TARGET THEN INSERT
    (TenantId, LangKey, Status, ZhCN, ZhTW, En, Ja, Ko)
    VALUES (s.TenantId, s.LangKey, ISNULL(s.Status, 'reviewed'), s.ZhCN, s.ZhTW, s.En, s.Ja, s.Ko);

DECLARE @n int = @@ROWCOUNT;
COMMIT TRAN;

PRINT CONCAT('词条 upsert 完成，影响行数 = ', @n);
