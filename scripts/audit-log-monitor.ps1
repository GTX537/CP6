# Sys_FieldAuditLog 容量监控(M-MES 终审 Important#1: 上线前至少容量告警)
# 计划任务 CP6-AuditLog-Monitor 每 4h 运行;日志 C:\CP6Backups\audit-log-monitor.log
# 阈值超限只告警不删数据——retention/归档策略本体待用户裁决(plan 文末 M-MES 票#1)
$ErrorActionPreference = "Stop"
$logFile = "C:\CP6Backups\audit-log-monitor.log"
$rowWarn = 1000000   # 100万行告警
$mbWarn  = 500       # 500MB 告警

try {
    $pw = (Get-Content "C:\CP6\.env" | Where-Object { $_ -match "^MSSQL_SA_PASSWORD=" }) -replace "^MSSQL_SA_PASSWORD=", ""
    $q = "SET NOCOUNT ON; SELECT p.rows, CAST(SUM(a.total_pages)*8/1024.0 AS DECIMAL(12,1)) AS mb FROM sys.tables t JOIN sys.partitions p ON t.object_id=p.object_id AND p.index_id IN (0,1) JOIN sys.allocation_units a ON p.partition_id=a.container_id WHERE t.name='Sys_FieldAuditLogs' GROUP BY p.rows;"
    $out = wsl -e docker exec cp6-db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$pw" -C -d CP6DB -h -1 -Q "$q" 2>&1 | Where-Object { $_ -match "\d" } | Select-Object -First 1
    $parts = ($out -split "\s+") | Where-Object { $_ -ne "" }
    $rows = [long]$parts[0]; $mb = [decimal]$parts[1]
    $level = "OK"
    if ($rows -gt $rowWarn -or $mb -gt $mbWarn) { $level = "WARN" }
    $line = "{0} [{1}] Sys_FieldAuditLogs rows={2} size={3}MB (warn: rows>{4} or size>{5}MB)" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $level, $rows, $mb, $rowWarn, $mbWarn
    Add-Content -Path $logFile -Value $line
    if ($level -eq "WARN") {
        Add-Content -Path $logFile -Value "  !! 超阈值: 需执行归档/purge 裁决(见 docs/superpowers/plans/2026-07-07-module-waves-crosscutting.md M-MES 票#1)"
    }
} catch {
    Add-Content -Path $logFile -Value ("{0} [ERROR] {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $_.Exception.Message)
}
