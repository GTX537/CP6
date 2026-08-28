# Space 托管 DXF Parser 50 MiB 容量合同

> 当前口径：本文后续提到的 Primary/Backup 双链复测是历史要求。
> Lean Core GA Schema 3 与 `cad-provider-adr-0001-v2` 只要求一个合格
> Primary；50 MiB 容量和性能门槛本身不变。

日期：2026-08-27

## 结论

托管 DXF Parser 已从 25 MiB 整文件缓冲实现升级为 64 MiB 失败关闭上限，并通过一个精确 50 MiB 的合法 DXF 容量包络。解析期间在底层字节流上同步计数与 SHA-256，使用严格 UTF-8 逐行读取，不再同时保留原始 byte[]、整份解码文本和 Split 行数组。DXF 999 注释仍会完整解码与计入哈希/大小，但因没有语义作用而不保留其文本。

Converter Version 从 `cp6-development-dxf/1.0.0` 升为 `1.1.0`；AutoCAD 候选的组合版本因此自动变为 `{core-version}+cp6-dxf-1.1.0`。旧 Site 认证/批准 Manifest 不会静默复用，必须重新评分和认证。

这只关闭 50 MiB 输入容量的仓库合同，不是正式性能验收。50 MiB 输入由有效 DXF + 大型 999 注释构成，是合成容量包络，不是授权真实复杂仓库 CAD；没有记录 Ready P95、CPU/峰值内存、准确率或人工修正结果。因此 WP3/WP7 保持 `Partial/Pending`，Space 总体保持 72% / `NoGo`。

## 实现边界

- Seekable 输入在读取前按剩余长度拒绝 `64 MiB + 1`；不创建 CAD IR 工件。
- 非 Seekable 输入也由同一 bounded hashing stream 在读取中执行 64 MiB 上限。
- SHA-256 覆盖原始字节，包括 CR/LF 形式与 999 注释；完整流读完且哈希匹配后才构建 CAD IR 和写 Sink。
- 继续严格拒绝无效 UTF-8、奇数行、非法 Group Code、缺失 `0/EOF`、错误源哈希和未知单位阻断场景。
- 64 MiB 是当前候选 Parser 上限，不扩大远程协议的 200 MiB 上限，也不代表任意 200 MiB DXF 可被该候选接受。

## 验证

| 门禁 | 结果 |
|---|---:|
| 精确 50 MiB 合法 DXF 包络 | 通过，1 个实体，源 SHA 一致 |
| 64 MiB + 1 seekable 输入 | 解析前拒绝，无输出工件 |
| 完整 CAD Experiment + 两项安装型门禁 | 47/47，0 skipped |
| 真实 Core Console DWG 回归 | 29 图层、19 块、4,424 实体、4,422 支持实体 |
| 安装测试根残留 DWG/DXF / Attempt | 0 / 0 |
| `CP6.Tests` | 2,939 passed / 19 environment-gated skipped / 0 failed |
| `CP6.slnx` Release | 0 warning / 0 error |

合并候选还必须通过 GA 证据门禁、远程 required checks 与 post-merge 冒烟；最终结果以 PR 记录为准。

## 仍需真实关闭

1. 使用授权的真实 50 MiB 标准 DXF 和 DWG（含 DWG 导出的中间 DXF）记录墙钟、CPU、峰值内存、Ready P95 与准确率。
2. 在冻结的 Primary/Backup 两条链上运行相同 20 份 10/5/5 黄金集；两者都达到 ADR-0001 80 分及正式质量门槛。
3. 将 `development` 组合链冻结为内容寻址 Release，并重新生成批准 Manifest、Site 认证和部署证据。

上述运行和签署可由同一实名 `DeliveryOwner` 完成，不要求多人门禁。
