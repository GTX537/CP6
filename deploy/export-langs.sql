/* ============================================================================
 * export-langs.sql — 导出全部多语言词条 Sys_Lang 为可版本控制的 JSON 资产
 * ----------------------------------------------------------------------------
 * 用途：把词条从活库导出进仓库 deploy/seed-data/sys_lang.json，做部署 fallback +
 *       灾备 + 可 diff 的版本历史（详见 deploy/runbook.md §5）。
 *
 * 运行（容器内，-y 0 取消列宽截断，-h -1 去表头，输出整段 JSON 到文件）：
 *   docker exec cp6-db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C \
 *     -y 0 -h -1 -i /var/opt/mssql/backup/export-langs.sql \
 *     -o /var/opt/mssql/backup/sys_lang.json
 *   # 再 docker cp 回 deploy/seed-data/ 提交
 *
 * 说明：外层包一层 SELECT(…FOR JSON…) 成单一标量 → sqlcmd -y 0 拿到完整一段 JSON，
 *       避免 FOR JSON 直出时按 2033 字符切成多行的坑。
 * ========================================================================== */

SET NOCOUNT ON;

SELECT (
    SELECT
        LangKey,
        TenantId,        -- null = 全局默认；非 null = 该租户覆盖
        Status,          -- reviewed / draft
        ZhCN, ZhTW, En, Ja, Ko
    FROM dbo.Sys_Langs
    ORDER BY
        CASE WHEN TenantId IS NULL THEN 0 ELSE 1 END,  -- 全局在前
        TenantId,
        LangKey
    FOR JSON PATH, INCLUDE_NULL_VALUES
) AS LangJson;
