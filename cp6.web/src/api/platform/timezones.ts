// E-T2 租户时区下拉候选 + 规整。后端 TimeZoneInfo.FindSystemTimeZoneById（.NET 8）跨平台容纳
// IANA/Windows 双制式 → 这里用 IANA id 作 value（可移植、日本团队常用），label 为人读时区名。
// 非白名单 id 仍可由后端接受（校验才是真闸门 E-WF-028）；下拉只是常用捷径，不是完备集。

export interface TimeZoneOption {
  /** TimeZoneInfo id（IANA），提交给后端保存到 Sys_Tenant.TimeZoneId */
  value: string
  /** 人读标签（含 UTC 偏移提示，静态；夏令时地区偏移随季节变，仅作参考） */
  label: string
}

export const TIMEZONE_OPTIONS: TimeZoneOption[] = [
  { value: 'Asia/Tokyo', label: '(UTC+09:00) 日本 東京' },
  { value: 'Asia/Shanghai', label: '(UTC+08:00) 中国 上海' },
  { value: 'Asia/Taipei', label: '(UTC+08:00) 台北' },
  { value: 'Asia/Seoul', label: '(UTC+09:00) 韩国 首尔' },
  { value: 'Asia/Hong_Kong', label: '(UTC+08:00) 香港' },
  { value: 'Asia/Singapore', label: '(UTC+08:00) 新加坡' },
  { value: 'Asia/Bangkok', label: '(UTC+07:00) 曼谷' },
  { value: 'Asia/Kolkata', label: '(UTC+05:30) 印度 加尔各答' },
  { value: 'Asia/Dubai', label: '(UTC+04:00) 迪拜' },
  { value: 'Europe/London', label: '(UTC+00:00) 伦敦' },
  { value: 'Europe/Paris', label: '(UTC+01:00) 巴黎' },
  { value: 'Europe/Berlin', label: '(UTC+01:00) 柏林' },
  { value: 'Europe/Moscow', label: '(UTC+03:00) 莫斯科' },
  { value: 'America/New_York', label: '(UTC-05:00) 纽约' },
  { value: 'America/Chicago', label: '(UTC-06:00) 芝加哥' },
  { value: 'America/Denver', label: '(UTC-07:00) 丹佛' },
  { value: 'America/Los_Angeles', label: '(UTC-08:00) 洛杉矶' },
  { value: 'America/Sao_Paulo', label: '(UTC-03:00) 圣保罗' },
  { value: 'Australia/Sydney', label: '(UTC+10:00) 悉尼' },
  { value: 'Pacific/Auckland', label: '(UTC+12:00) 奥克兰' },
  { value: 'UTC', label: '(UTC+00:00) UTC' },
]

/** 提交前规整：trim；空 → null（视作清空，沿用 app 默认时区）。校验交后端（E-WF-028）。 */
export function normalizeTimeZoneId(v: string | null | undefined): string | null {
  if (v == null) return null
  const t = v.trim()
  return t.length === 0 ? null : t
}
