# ============================================================
# CP6 WMS Phase 1 — 一键启动脚本
# ============================================================
# 用途：
#   1. 应用 EF Migration（AddWmsCore）到本地 SQL Server
#   2. 插入 WMS 菜单种子数据（docs/wms-menu-seed.sql）
#   3. 编译后端 + 启动 dotnet run（后台）
#   4. 启动前端 vite dev server（后台）
#   5. 打印访问 URL
#
# 前提：
#   - .NET 8/10 SDK 已安装（dotnet ef tool）
#   - SQL Server LocalDB 或命名实例已运行
#   - Node.js + npm 已安装
#   - 已 git pull 到包含 Phase WM-1 代码的版本
#
# 使用（Windows PowerShell 5.1 或 PowerShell 7 どちらも可）：
#   powershell -ExecutionPolicy Bypass -File .\start-wms-phase1.ps1
#   powershell -ExecutionPolicy Bypass -File .\start-wms-phase1.ps1 -SkipMigration
#   powershell -ExecutionPolicy Bypass -File .\start-wms-phase1.ps1 -SkipSeed
#   powershell -ExecutionPolicy Bypass -File .\start-wms-phase1.ps1 -SkipFrontend
#   powershell -ExecutionPolicy Bypass -File .\start-wms-phase1.ps1 -SqlServer "localhost\KOUSQLSERVER" -Database "CP6DB"
#
# PowerShell ウィンドウから直接：
#   cd D:\CP6
#   .\start-wms-phase1.ps1
# ============================================================

[CmdletBinding()]
param(
    [string]$SqlServer  = "localhost\KOUSQLSERVER",
    [string]$Database   = "CP6DB",
    # 既定 5177 はプロジェクト規約（cp6.web/vite.config.ts proxy target と一致）。
    # Docker Compose と同居する場合は 9991 が占有されているため絶対に避ける。
    [string]$BackendUrl = "http://localhost:5177",
    [switch]$SkipMigration,
    [switch]$SkipSeed,
    [switch]$SkipBackend,
    [switch]$SkipFrontend,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$ProjectRoot = $PSScriptRoot
Set-Location $ProjectRoot

function Write-Step([string]$msg) {
    Write-Host ""
    Write-Host "=== $msg ===" -ForegroundColor Cyan
}

function Write-Ok([string]$msg)   { Write-Host "  [OK] $msg"   -ForegroundColor Green }
function Write-Warn([string]$msg) { Write-Host "  [WARN] $msg" -ForegroundColor Yellow }
function Write-Err([string]$msg)  { Write-Host "  [ERR] $msg"  -ForegroundColor Red }

# ─────────────────────────────────────────────────────────────
# 0. 前置チェック
# ─────────────────────────────────────────────────────────────
Write-Step "前置チェック"

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { Write-Err "dotnet が見つかりません。.NET SDK をインストールしてください。"; exit 1 }
Write-Ok "dotnet $(dotnet --version)"

$sqlcmd = Get-Command sqlcmd -ErrorAction SilentlyContinue
if (-not $sqlcmd -and -not $SkipSeed) {
    Write-Warn "sqlcmd が見つかりません。-SkipSeed を指定するか、SQL Server Tools をインストールしてください。"
}

if (-not $SkipFrontend) {
    $npm = Get-Command npm -ErrorAction SilentlyContinue
    if (-not $npm) { Write-Err "npm が見つかりません。Node.js をインストールしてください。"; exit 1 }
    Write-Ok "npm $(npm --version)"
}

# dotnet ef tool（必要時インストール）
if (-not $SkipMigration) {
    $efInstalled = (dotnet tool list -g) -match "dotnet-ef"
    if (-not $efInstalled) {
        Write-Warn "dotnet-ef がグローバルインストールされていません。インストール中..."
        dotnet tool install --global dotnet-ef 2>&1 | Out-Null
        $efInstalled = (dotnet tool list -g) -match "dotnet-ef"
        if (-not $efInstalled) { Write-Err "dotnet-ef のインストールに失敗しました。"; exit 1 }
    }
    Write-Ok "dotnet-ef tool 利用可能"
}

# ─────────────────────────────────────────────────────────────
# 1. EF Migration
# ─────────────────────────────────────────────────────────────
if (-not $SkipMigration) {
    Write-Step "1. EF Migration 適用（AddWmsCore）"
    Write-Host "  対象: Server=$SqlServer / Database=$Database"

    $efArgs = @(
        "database", "update",
        "--project", (Join-Path $ProjectRoot "CP6.Core"),
        "--startup-project", (Join-Path $ProjectRoot "CP6.WebApi")
    )
    if ($NoBuild) { $efArgs += "--no-build" }
    dotnet ef @efArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Err "Migration に失敗しました。接続文字列・SQL Server の状態を確認してください。"
        exit 1
    }
    Write-Ok "Migration 適用完了"
} else {
    Write-Warn "Migration スキップ"
}

# ─────────────────────────────────────────────────────────────
# 2. WMS メニュー シードデータ
# ─────────────────────────────────────────────────────────────
if (-not $SkipSeed -and $sqlcmd) {
    Write-Step "2. WMS メニュー シード投入"
    $seedSql = Join-Path $ProjectRoot "docs\wms-menu-seed.sql"
    if (-not (Test-Path $seedSql)) {
        Write-Err "シードファイルが見つかりません: $seedSql"
        exit 1
    }

    # Windows 認証で接続（Trusted_Connection）。SQL 認証が必要な場合は -U / -P を追加。
    # -f 65001 で入力ファイルを UTF-8 として読む（既定の OS ANSI コードページだと
    # 日本語/中国語の N'...' リテラルが mojibake になり構文エラーになる）。
    sqlcmd -S $SqlServer -d $Database -E -f 65001 -i $seedSql -b
    if ($LASTEXITCODE -ne 0) {
        Write-Err "シード投入失敗。-SkipSeed を指定するか SQL ファイルを確認してください。"
        exit 1
    }
    Write-Ok "メニュー 41 件（ID 400~499）投入完了"

    # 2b. MES + WMS i18n 词条（nav.300~312 + nav.400~481）
    $i18nSql = Join-Path $ProjectRoot "docs\mes-wms-i18n-seed.sql"
    if (Test-Path $i18nSql) {
        Write-Host "  i18n 词条投入中（MES + WMS、5 言語）..."
        sqlcmd -S $SqlServer -d $Database -E -f 65001 -i $i18nSql -b
        if ($LASTEXITCODE -ne 0) {
            Write-Err "i18n シード投入失敗。"
            exit 1
        }
        Write-Ok "i18n 词条 54 件投入完了（nav.300~312 + nav.400~481）"
    } else {
        Write-Warn "i18n シードファイルなし: $i18nSql"
    }
} elseif (-not $SkipSeed) {
    Write-Warn "sqlcmd が無いため、シードはスキップします。手動で SSMS で以下を実行してください："
    Write-Warn "  1) docs\wms-menu-seed.sql"
    Write-Warn "  2) docs\mes-wms-i18n-seed.sql"
} else {
    Write-Warn "シード スキップ"
}

# ─────────────────────────────────────────────────────────────
# 3. バックエンド ビルド + 起動
# ─────────────────────────────────────────────────────────────
if (-not $SkipBackend) {
    Write-Step "3. バックエンド起動（dotnet run、バックグラウンド）"

    if (-not $NoBuild) {
        dotnet build (Join-Path $ProjectRoot "CP6.WebApi\CP6.WebApi.csproj") --nologo
        if ($LASTEXITCODE -ne 0) { Write-Err "ビルド失敗"; exit 1 }
        Write-Ok "ビルド成功"
    }

    # ポート衝突チェック（Docker Desktop が 9991 を保持しがち、本機の場合は 5177 も含む）
    $port = ([Uri]$BackendUrl).Port
    $occupied = (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)
    if ($occupied) {
        $owners = $occupied | ForEach-Object {
            try {
                $p = Get-Process -Id $_.OwningProcess -ErrorAction Stop
                "PID=$($p.Id) Name=$($p.ProcessName)"
            } catch { "PID=$($_.OwningProcess) (Process info unavailable)" }
        } | Sort-Object -Unique
        Write-Err "ポート $port が既に使用中です: $($owners -join '; ')"
        Write-Err "  対処1: 占有プロセスを停止する（dotnet なら Stop-Process）"
        Write-Err "  対処2: Docker 由来なら 'docker compose down' を実行"
        Write-Err "  対処3: -BackendUrl http://localhost:5178 など別ポートを指定"
        Write-Err "  ※ vite.config.ts の proxy target も合わせて変更が必要"
        exit 1
    }

    $backendLog = Join-Path $ProjectRoot "backend.log"
    $backend = Start-Process -FilePath "dotnet" `
        -ArgumentList "run --project CP6.WebApi/CP6.WebApi.csproj --no-build --urls $BackendUrl" `
        -WorkingDirectory $ProjectRoot `
        -RedirectStandardOutput $backendLog `
        -RedirectStandardError (Join-Path $ProjectRoot "backend.err.log") `
        -PassThru -WindowStyle Hidden
    Write-Ok "バックエンド PID=$($backend.Id) — ログ: backend.log"

    # ヘルスチェック：最大 30 秒待って起動を確認
    $ready = $false
    for ($i = 0; $i -lt 15; $i++) {
        Start-Sleep -Seconds 2
        # プロセスが既に死んでいないかチェック
        if ($backend.HasExited) {
            Write-Err "バックエンドプロセスが起動直後に終了しました（ExitCode=$($backend.ExitCode)）。"
            Write-Err "  backend.err.log の末尾を確認してください："
            if (Test-Path (Join-Path $ProjectRoot "backend.err.log")) {
                Get-Content (Join-Path $ProjectRoot "backend.err.log") -Tail 10 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
            }
            exit 1
        }
        try {
            $null = Invoke-WebRequest -Uri "$BackendUrl/swagger/index.html" -TimeoutSec 2 -UseBasicParsing -ErrorAction Stop
            $ready = $true
            break
        } catch { }
    }
    if ($ready) {
        Write-Ok "バックエンド HTTP 応答確認（$BackendUrl）"
    } else {
        Write-Warn "30 秒待っても $BackendUrl が応答しません。backend.log を確認してください。"
    }
} else {
    Write-Warn "バックエンド スキップ"
}

# ─────────────────────────────────────────────────────────────
# 4. フロントエンド起動
# ─────────────────────────────────────────────────────────────
if (-not $SkipFrontend) {
    Write-Step "4. フロントエンド起動（npm run dev、バックグラウンド）"
    $frontDir = Join-Path $ProjectRoot "cp6.web"
    if (-not (Test-Path (Join-Path $frontDir "node_modules"))) {
        Write-Warn "node_modules 未インストール。npm install 実行中..."
        Push-Location $frontDir
        npm install
        Pop-Location
    }

    $frontLog = Join-Path $ProjectRoot "frontend.log"
    $frontend = Start-Process -FilePath "npm" `
        -ArgumentList "run dev" `
        -WorkingDirectory $frontDir `
        -RedirectStandardOutput $frontLog `
        -RedirectStandardError (Join-Path $ProjectRoot "frontend.err.log") `
        -PassThru -WindowStyle Hidden
    Write-Ok "フロントエンド PID=$($frontend.Id) — ログ: frontend.log"
    Start-Sleep -Seconds 3
} else {
    Write-Warn "フロントエンド スキップ"
}

# ─────────────────────────────────────────────────────────────
# 5. 完了表示
# ─────────────────────────────────────────────────────────────
Write-Step "起動完了"
Write-Host ""
Write-Host "  Backend  : $BackendUrl/swagger" -ForegroundColor Cyan
Write-Host "  Frontend : http://localhost:5173" -ForegroundColor Cyan
Write-Host ""
Write-Host "  WMS パス（管理者ログイン後）:" -ForegroundColor Yellow
Write-Host "    /wms/warehouse    倉庫マスタ"
Write-Host "    /wms/stock        在庫照会"
Write-Host ""
Write-Host "  ログ確認:" -ForegroundColor Gray
Write-Host "    Get-Content backend.log -Wait"
Write-Host "    Get-Content frontend.log -Wait"
Write-Host ""
Write-Host "  停止方法（PID 直接 kill or 全 dotnet/node プロセス）:" -ForegroundColor Gray
if ($backend)  { Write-Host "    Stop-Process -Id $($backend.Id)" }
if ($frontend) { Write-Host "    Stop-Process -Id $($frontend.Id)" }
Write-Host ""
