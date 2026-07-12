# WU 下载缓存守卫(7/12 与本日两次磁盘满事故的自愈闸)
# 计划任务 CP6-WUCache-Guard 每 4h: WU Download 缓存 >500MB 或 C 盘余量 <4GB 时清缓存
# 只清 SoftwareDistribution\Download(WU 会按需重下,微软官方认可的安全清理点),不动其他
$ErrorActionPreference = "Stop"
$logFile = "C:\CP6Backups\wu-cache-guard.log"
$dl = "C:\Windows\SoftwareDistribution\Download"
try {
    $freeGB = [math]::Round((Get-PSDrive C).Free / 1GB, 2)
    $sizeMB = 0
    if (Test-Path $dl) {
        $sum = (Get-ChildItem $dl -Recurse -Force -File -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum
        if ($null -eq $sum) { $sum = 0 }
        $sizeMB = [math]::Round($sum / 1MB, 0)
    }
    if ($sizeMB -gt 500 -or ($freeGB -lt 4 -and $sizeMB -gt 50)) {
        try { Stop-Service wuauserv -Force -ErrorAction Stop } catch {}
        Remove-Item "$dl\*" -Recurse -Force -Confirm:$false -ErrorAction SilentlyContinue
        try { Start-Service wuauserv -ErrorAction Stop } catch {}
        $freeAfter = [math]::Round((Get-PSDrive C).Free / 1GB, 2)
        Add-Content $logFile ("{0} [CLEANED] WU cache {1}MB purged; C free {2}GB -> {3}GB" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $sizeMB, $freeGB, $freeAfter)
    } else {
        Add-Content $logFile ("{0} [OK] WU cache {1}MB, C free {2}GB" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $sizeMB, $freeGB)
    }
} catch {
    Add-Content $logFile ("{0} [ERROR] {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $_.Exception.Message)
}
