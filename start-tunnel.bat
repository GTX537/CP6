@echo off
REM ============================================================
REM  CP6 内网穿透（Cloudflare Tunnel）启动
REM  - 清掉本机代理环境变量，避免 cloudflared 走 xray(10808)
REM  - config.yml 已设 protocol: http2
REM  注意：若 xray 处于「全局/TUN」模式，会在系统层拦截 7844 端口，
REM        本脚本无法绕过 —— 需在 xray 里给 argotunnel/Cloudflare 加直连，
REM        或先把 xray 切到「规则/PAC」模式 / 临时关闭。
REM ============================================================
setlocal
set "HTTP_PROXY="
set "HTTPS_PROXY="
set "http_proxy="
set "https_proxy="
set "ALL_PROXY="

set "CF=C:\Users\tt\AppData\Local\Microsoft\WinGet\Packages\Cloudflare.cloudflared_Microsoft.Winget.Source_8wekyb3d8bbwe\cloudflared.exe"
if not exist "%CF%" set "CF=cloudflared"

echo [CP6] 启动 cloudflared 隧道 675bc4d4-... (http2)
echo [CP6] 若一直报 TLS handshake EOF，请在 xray 里给 Cloudflare 加直连，或临时关闭代理。
echo.
"%CF%" tunnel run 675bc4d4-18b5-41ac-9724-894c86be91c5
echo.
echo [CP6] 隧道已退出。按任意键关闭窗口。
pause >nul
endlocal
