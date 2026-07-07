# =====================================================================
# WSL keep-alive（2026-07-07 固化为登录自启，闭掉 new-env-setup 记忆里的 TODO）
#
# 背景：WSL2 生命周期跟客户端连接走——最后一个 wsl.exe 退出后约 1 分钟整机
#       poweroff，dockerd/容器/systemd 单元全部消失（症状=每条 wsl 命令看到的
#       都是刚开机 4 秒的容器）。必须常驻一个隐藏 wsl 会话钉住 VM。
# 幂等：已有 keep-alive 存活则跳过。
# 调度：计划任务 CP6-WSL-KeepAlive（ONLOGON 触发）；手动执行同样安全。
# =====================================================================
$existing = Get-CimInstance Win32_Process -Filter "Name = 'wsl.exe'" |
    Where-Object { $_.CommandLine -match 'sleep\s+infinity' }
if ($existing) {
    Write-Output "keep-alive 已存活（PID $($existing.ProcessId -join ', ')），跳过"
    exit 0
}
Start-Process -WindowStyle Hidden wsl.exe -ArgumentList '-d','Ubuntu','-u','root','--','sleep','infinity'
Write-Output "keep-alive 已拉起"
