# ============================================================
# CP6 本地穿透 完整 redeploy（Docker 模式）
# ============================================================
# 用途：
#   1. 重 build cp6-api + cp6-web（包含本地最新 WMS 代码）
#   2. 等 API 跑完 EF Migration（自动应用 5 个 WMS migration）
#   3. 把 8 个 SQL seed 拷进 cp6-db 容器并执行
#      （菜单 + i18n 各模块 = WMS 全套词条 ~350 条）
#   4. 重启 cp6-api 让 Lang Cache flush
#   5. 提示用户启动 cloudflared（前台）
#
# 前提：Docker Desktop 已在跑（你电脑确认已经 3 天稳定运行）
# 注意：DB volume 数据保留，只 build 新 api/web 镜像
# 执行：powershell -ExecutionPolicy Bypass -File .\redeploy-cp6uk.ps1
# ============================================================

[CmdletBinding()]
param(
    [switch]$SkipBuild,     # 跳过 docker build（只跑 seed）
    [switch]$SkipSeed,      # 跳过 SQL seed
    [switch]$SkipCloudflared # 不提示启动 cloudflared
)

$ErrorActionPreference = "Stop"
$ProjectRoot = $PSScriptRoot
Set-Location $ProjectRoot

function Write-Step([string]$msg) {
    Write-Host ""
    Write-Host "=== $msg ===" -ForegroundColor Cyan
}
function Write-Ok([string]$m)   { Write-Host "  [OK] $m"   -ForegroundColor Green }
function Write-Warn([string]$m) { Write-Host "  [WARN] $m" -ForegroundColor Yellow }
function Write-Err([string]$m)  { Write-Host "  [ERR] $m"  -ForegroundColor Red }

# ─────────────────────────────────────────────────────────────
# 0. 前置チェック
# ─────────────────────────────────────────────────────────────
Write-Step "0. 前置チェック"

$docker = Get-Command docker -ErrorAction SilentlyContinue
if (-not $docker) { Write-Err "docker CLI が無い。Docker Desktop を起動してください"; exit 1 }
Write-Ok "docker $(docker version --format '{{.Server.Version}}')"

# DB 容器が走っているか
$dbState = docker ps --filter "name=cp6-db" --filter "status=running" --format '{{.Names}}'
if (-not $dbState) { Write-Err "cp6-db 容器が動いていません。'docker compose up -d cp6-db' を先に"; exit 1 }
Write-Ok "cp6-db running"

# ─────────────────────────────────────────────────────────────
# 1. Docker build & up（cp6-api + cp6-web のみ）
# ─────────────────────────────────────────────────────────────
if (-not $SkipBuild) {
    Write-Step "1. Docker build cp6-api + cp6-web（最新コード反映）"
    Write-Host "  build 中... 5-15 分かかる可能性あり（.NET SDK / npm install）"
    docker compose up -d --build --no-deps cp6-api cp6-web
    if ($LASTEXITCODE -ne 0) { Write-Err "build 失败"; exit 1 }
    Write-Ok "build 完了"

    Write-Step "1.5 cp6-api がヘルシーになるまで待機（最大 60 秒）"
    $ready = $false
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Seconds 2
        try {
            $r = Invoke-WebRequest -Uri "http://localhost:9991/swagger/index.html" -TimeoutSec 2 -UseBasicParsing -ErrorAction Stop
            if ($r.StatusCode -eq 200) { $ready = $true; break }
        } catch { }
    }
    if ($ready) { Write-Ok "cp6-api HTTP 200" }
    else { Write-Warn "60 秒待っても応答なし。継続するが docker logs cp6-api を確認" }
} else {
    Write-Warn "Build スキップ"
}

# ─────────────────────────────────────────────────────────────
# 2. SQL Seed を docker DB に投入
# ─────────────────────────────────────────────────────────────
if (-not $SkipSeed) {
    Write-Step "2. WMS SQL Seed を cp6-db に投入"

    $seeds = @(
        @{ File = "wms-menu-seed.sql"; Desc = "WMS 菜单 (ID 400-499)" }
        @{ File = "mes-wms-i18n-seed.sql"; Desc = "nav.300-481 (54 keys × 5 langs)" }
        @{ File = "wms-views-i18n-seed.sql"; Desc = "wms.common/warehouse/inbound/outbound/* (253)" }
        @{ File = "wms-phase5-i18n-seed.sql"; Desc = "wms.qc/rma/expiry.* (63)" }
        @{ File = "wms-realtime-i18n-seed.sql"; Desc = "wms.dashboard.realtime.* (9)" }
        @{ File = "wms-lot-trace-i18n-seed.sql"; Desc = "wms.lotTrace.* (17)" }
        @{ File = "wms-kitting-i18n-seed.sql"; Desc = "wms.kit.* (29)" }
    )

    foreach ($s in $seeds) {
        $localPath = Join-Path $ProjectRoot "docs\$($s.File)"
        if (-not (Test-Path $localPath)) {
            Write-Warn "  $($s.File) なし、スキップ"
            continue
        }
        Write-Host "  → $($s.File)：$($s.Desc)"

        # 1. ファイルを cp6-db に コピー
        docker cp $localPath "cp6-db:/tmp/$($s.File)" | Out-Null
        # 2. sqlcmd で実行（UTF-8 BOM 付きで -f 65001 を強制）
        $cmd = "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Cp6@Docker2024!' -C -d CP6DB -f 65001 -i /tmp/$($s.File) -b"
        $out = docker exec cp6-db bash -c $cmd 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Err "  失敗：$out"
            exit 1
        }
        # 末尾 1-2 行（追加件数）を表示
        $out | Select-Object -Last 3 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
    }
    Write-Ok "全 SQL Seed 投入完了"

    # 3. Lang Cache flush のため cp6-api 再起動
    Write-Step "2.5 cp6-api 再起動（Lang Cache flush）"
    docker compose restart cp6-api | Out-Null
    Start-Sleep -Seconds 8
    Write-Ok "cp6-api 再起動完了"
} else {
    Write-Warn "Seed スキップ"
}

# ─────────────────────────────────────────────────────────────
# 3. cloudflared 起動案内
# ─────────────────────────────────────────────────────────────
Write-Step "3. cloudflared 起動"

$cf = (Get-Command cloudflared -ErrorAction SilentlyContinue).Source
if (-not $cf) {
    $cf = "C:\Users\tt\AppData\Local\Microsoft\WinGet\Packages\Cloudflare.cloudflared_Microsoft.Winget.Source_8wekyb3d8bbwe\cloudflared.exe"
}

$running = Get-Process -Name cloudflared -ErrorAction SilentlyContinue
if ($running) {
    Write-Ok "cloudflared 既に起動済 (PID=$($running.Id))"
} elseif (-not $SkipCloudflared) {
    Write-Host ""
    Write-Host "  以下のコマンドで cloudflared を起動してください（新しい cmd ウィンドウで）:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "    `"$cf`" tunnel run 675bc4d4-18b5-41ac-9724-894c86be91c5" -ForegroundColor White
    Write-Host ""
    Write-Host "  または、本ウィンドウで前台跑（窓を閉じると止まる）:" -ForegroundColor Gray
    Write-Host "    （Enter を押すと起動、Ctrl+C で停止）" -ForegroundColor Gray
    $ans = Read-Host "  今 起動しますか？ [y/N]"
    if ($ans -eq 'y' -or $ans -eq 'Y') {
        & $cf tunnel run 675bc4d4-18b5-41ac-9724-894c86be91c5
    }
}

Write-Step "完了"
Write-Host ""
Write-Host "  本地アクセス：" -ForegroundColor Cyan
Write-Host "    Frontend  : http://localhost:8080" -ForegroundColor White
Write-Host "    API       : http://localhost:9991/swagger" -ForegroundColor White
Write-Host ""
Write-Host "  外部アクセス（cloudflared 起動後）：" -ForegroundColor Cyan
Write-Host "    https://cp6.uk/login" -ForegroundColor White
Write-Host "    https://api.cp6.uk/swagger" -ForegroundColor White
Write-Host ""
