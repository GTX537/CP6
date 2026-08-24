# Space AutoCAD Core Console 开发转换链报告

日期：2026-08-15

## 结论

本机 `D:\AutoCAD 2025\accoreconsole.exe` 已通过 AutoCAD 2025 原生 DWG → ASCII DXF → CP6 CAD IR 的开发合同冒烟。仓库新增的开发适配器继续通过 `SpaceCadConverterContractRunner`，不会注册为 Site 生产 Provider，也不改变核心 GA 的 72% / `NoGo` 状态。

## 本机输入与身份

| 项目 | 结果 |
|---|---|
| Core Console 版本 | `25.0.58.0.0` |
| Core Console SHA-256 | `d1fd7232893094234f31c65445d0ec9259ffc1df17fb15aad99373e31545cefb` |
| Core Console Authenticode | `Valid`，Autodesk, Inc. |
| 开发样例 | AutoCAD 安装目录自带 `Sample\Database Connectivity\Floor Plan Sample.dwg` |
| 样例大小 | 231,872 bytes |
| 样例 SHA-256 | `19270c23e56e407aab2ade3644e8f301c34e390638d99c3f0cc4f2d3a6516792` |
| 证据类别 | `DevelopmentEvidence`；不计黄金 CAD |

原始 DWG、DXF 和生成 CAD IR 均未提交仓库。

## 两次确定性运行

两次独立转换得到相同 CAD IR SHA-256：

`c3cba311b3d3663a32d978a4d4ee9d2a402e9d8724a1e9ee76e7c23b9f842e5a`

每次结果均为 29 个图层、19 个块、4,424 个实体，其中 4,422 个受支持，2 个 `VIEWPORT` 作为显式 Warning 保留；没有缺失 SourceRef。源单位识别为 Inch 并记录 `25.4` 毫米比例。最终复跑后 `attempts` 中原始/中间文件数为 0，持久 Autodesk 运行缓存中的 DWG/DXF 数也为 0。单次命令墙钟约 6～7 秒，其中包含 `dotnet run` 启动，不是 50MB P95 性能证据。

安装型门禁使用以下三个环境变量运行并实际通过 1/1、0 skipped：

```text
CP6_TEST_AUTOCAD_CORE_CONSOLE=D:\AutoCAD 2025\accoreconsole.exe
CP6_TEST_AUTOCAD_DWG=<local authorized or Autodesk development sample DWG>
CP6_TEST_AUTOCAD_WORK_ROOT=<D-drive isolated work root>
```

## 安全和范围边界

- 原始流只读，先核对原始 DWG SHA-256，再调用 Core Console。
- 子进程不经 Shell；输入、脚本和中间 DXF 只进入每次唯一 `attempts` 目录并在结束后清除。
- Autodesk Activity Insights 会在 Core Console 退出后继续锁定自己的运行包和继承的工作目录，因此 Core Console 从 D 盘持久 `_autodesk-runtime-cache` 启动，与原始数据分离；该缓存一旦出现 DWG/DXF 就失败关闭。
- 子进程超时或取消时终止整个进程树；原始数据目录清理对临时文件锁有限重试，最终失败则任务失败关闭。
- Provider Version 直接绑定 `accoreconsole.exe` 的文件版本，版本漂移会导致请求拒绝。
- 未建立网络隔离、法务/客户批准、Site 主备认证、Secret/Worker 治理或黄金集评分，因此不能作为 WP3/WP7 正式接受证据。

另行观察到 GUI 主程序 `D:\AutoCAD 2025\acad.exe` 的本机 Authenticode 检查为 `HashMismatch`。本任务没有启动或调用该文件；当前开发链只使用签名有效的 Core Console。正式评测前应通过 Autodesk 更新/修复流程恢复 GUI 安装完整性或形成受批准的例外证据。
