# =====================================================================
# CP6 数据库定时备份（2026-07-07 数据安全纪律配套，见记忆 commit-push-immediately）
#
# 背景：SQL Server 跑在 WSL docker 容器 cp6-db 内，数据落在 WSL 虚拟盘上——
#       WSL/宿主机曾多次卡死近乎重装，库数据是本机唯一副本。
# 做法：在容器内 BACKUP DATABASE（压缩+校验）→ docker cp 拷出到 Windows 文件系统
#       C:\CP6Backups\ → 清理容器内临时文件 → 本地保留 14 天滚动。
# 凭据：运行时从 CP6.WebApi\appsettings.Local.json（gitignored）解析 sa 密码，
#       本脚本不含任何密钥，可安全入库。
# 调度：Windows 计划任务 CP6-DB-Backup 每 4 小时一次（注册命令见文件尾注释）。
#       手动执行：powershell -NoProfile -ExecutionPolicy Bypass -File C:\CP6\scripts\db-backup.ps1
# 还原示例（容器内）：
#   wsl docker cp C:\CP6Backups\CP6DB\<file>.bak cp6-db:/var/opt/mssql/restore.bak
#   wsl docker exec cp6-db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P <pw> -C -Q `
#     "RESTORE DATABASE [CP6DB] FROM DISK='/var/opt/mssql/restore.bak' WITH REPLACE"
# =====================================================================

$ErrorActionPreference = 'Stop'
$BackupRoot   = 'C:\CP6Backups'
$LogFile      = Join-Path $BackupRoot 'backup.log'
$RetentionDays = 14
$Container    = 'cp6-db'
$SqlcmdInCtr  = '/opt/mssql-tools18/bin/sqlcmd'
$LocalJson    = 'C:\CP6\CP6.WebApi\appsettings.Local.json'

if (-not (Test-Path $BackupRoot)) { New-Item -ItemType Directory -Path $BackupRoot | Out-Null }

function Log([string]$msg) {
    $line = "{0}  {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $msg
    Add-Content -Path $LogFile -Value $line -Encoding utf8
    Write-Output $line
}

try {
    # ── 凭据（运行时解析，不落盘不入库）─────────────────────────
    $cs = (Get-Content $LocalJson -Raw | ConvertFrom-Json).ConnectionStrings.DefaultConnection
    $pw = ($cs -split ';' | Where-Object { $_ -like 'Password=*' }) -replace '^Password=', ''
    if ([string]::IsNullOrEmpty($pw)) { throw "无法从 appsettings.Local.json 解析 sa 密码" }

    # ── 容器健康守卫（WSL 弹跳期跳过本轮，不 hang 计划任务）──────
    $state = (wsl docker inspect --format '{{.State.Status}}' $Container 2>$null)
    if ($state -ne 'running') { Log "SKIP: 容器 $Container 非 running（state=$state），本轮跳过"; exit 0 }
    $health = (wsl docker inspect --format '{{.State.Health.Status}}' $Container 2>$null)
    if ($health -and $health -ne 'healthy') { Log "SKIP: 容器 $Container 未 healthy（$health），本轮跳过"; exit 0 }

    # ── 自动发现业务库（CP6DB 前缀，排除系统库）──────────────────
    $dbListRaw = wsl docker exec $Container $SqlcmdInCtr -S localhost -U sa -P "$pw" -C -h -1 -W -Q "SET NOCOUNT ON; SELECT name FROM sys.databases WHERE name LIKE 'CP6DB%' ORDER BY name;"
    $dbs = @($dbListRaw | Where-Object { $_ -and $_.Trim() -match '^CP6DB' } | ForEach-Object { $_.Trim() })
    if ($dbs.Count -eq 0) { throw "未发现 CP6DB* 业务库" }

    # ── 库级就绪守卫：容器 healthy ≠ 库 ONLINE（启动恢复期 BACKUP 会 Msg 904/3023）──
    $ready = $false
    for ($i = 0; $i -lt 12; $i++) {
        $notOnline = wsl docker exec $Container $SqlcmdInCtr -S localhost -U sa -P "$pw" -C -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name LIKE 'CP6DB%' AND state_desc <> 'ONLINE';"
        if (($notOnline | Select-Object -First 1).Trim() -eq '0') { $ready = $true; break }
        Start-Sleep -Seconds 10
    }
    if (-not $ready) { Log "SKIP: 业务库超过 120s 未全部 ONLINE（启动恢复期），本轮跳过"; exit 0 }
    Log "开始备份：$($dbs -join ', ')"

    wsl docker exec $Container mkdir -p /var/opt/mssql/backup | Out-Null
    $ts = Get-Date -Format 'yyyyMMdd_HHmmss'
    $ok = 0

    foreach ($db in $dbs) {
        $bakName = "${db}_${ts}.bak"
        $ctrPath = "/var/opt/mssql/backup/$bakName"
        $destDir = Join-Path $BackupRoot $db
        if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir | Out-Null }

        # BACKUP 带一次重试（启动尾声偶发 Msg 3023 序列化冲突）
        $bakOk = $false
        for ($try = 1; $try -le 2; $try++) {
            wsl docker exec $Container $SqlcmdInCtr -S localhost -U sa -P "$pw" -C -b -Q "BACKUP DATABASE [$db] TO DISK='$ctrPath' WITH INIT, COMPRESSION, CHECKSUM;"
            if ($LASTEXITCODE -eq 0) { $bakOk = $true; break }
            Start-Sleep -Seconds 15
        }
        if (-not $bakOk) { Log "ERROR: $db BACKUP 失败（两次尝试）"; continue }

        # docker cp 目标须用 WSL 视角路径（C:\ 会被误解析为容器名）
        $destWin = Join-Path $destDir $bakName
        $destWsl = (wsl wslpath -a ($destWin -replace '\\', '/')).Trim()
        wsl docker cp "${Container}:$ctrPath" "$destWsl"
        if ($LASTEXITCODE -ne 0) { Log "ERROR: $db docker cp 拷出失败"; continue }
        wsl docker exec $Container rm -f $ctrPath | Out-Null

        $size = [math]::Round((Get-Item (Join-Path $destDir $bakName)).Length / 1MB, 1)
        Log "OK: $db → $destDir\$bakName（${size} MB）"
        $ok++
    }

    # ── 滚动保留 ────────────────────────────────────────────────
    $cutoff = (Get-Date).AddDays(-$RetentionDays)
    $purged = Get-ChildItem -Path $BackupRoot -Recurse -Filter '*.bak' | Where-Object { $_.LastWriteTime -lt $cutoff }
    if ($purged) { $purged | Remove-Item -Force; Log "清理 $($purged.Count) 份超过 ${RetentionDays} 天的旧备份" }

    Log "完成：$ok/$($dbs.Count) 库备份成功"
    if ($ok -lt $dbs.Count) { exit 1 }
}
catch {
    Log "FATAL: $($_.Exception.Message)"
    exit 1
}

# ── 计划任务注册（一次性，管理员 PowerShell 执行）────────────────
# schtasks /Create /TN "CP6-DB-Backup" /SC HOURLY /MO 4 /ST 03:00 /F ^
#   /TR "powershell -NoProfile -ExecutionPolicy Bypass -File C:\CP6\scripts\db-backup.ps1"
# 注：任务以当前用户"仅登录时运行"模式跑（WSL/docker 依赖用户会话）；本机为常驻登录服务器，可接受。
