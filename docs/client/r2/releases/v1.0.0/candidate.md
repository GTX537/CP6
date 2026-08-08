# v1.0.0 候选发布执行规范

## 1. 完成边界

本批次从生产输入冻结开始，以签名候选和 Compose 试点环境
`deployment-evidence.json` 通过并完成不可变归档为结束。R2A MOVE 开关、
两周现场试点、R2B 转换和 Kubernetes 多仓推广不属于本批次。

机器输入以同目录 `candidate.yaml` 为准。该文件只保存非敏感元数据和密钥库
引用；密码、私钥、连接串、令牌及证书内容禁止进入 Git。

## 2. 冻结与 Tag

Release Owner 只能在 Candidate 与 Compose 所需输入全部为 `Approved` 后运行
`R2 release freeze` 工作流。工作流在受保护 `r2-release-freeze`
Environment 内完成以下原子步骤：

1. 核对当前 `origin/main`、Desktop/Android 版本和 `v1.0.0` 不存在；
2. 对每项批准证据执行 S3 `head-object`，确认 VersionId、COMPLIANCE Object
   Lock，且证据生成时间不晚于批准时间；
3. 生成包含真实 main SHA 与 Spec SHA-256 的 `release-freeze.json`；
4. 以 SSE、版本控制和 Object Lock 归档冻结快照；
5. 使用最小权限 GitHub App 创建 annotated `v1.0.0` Tag。

Tag Ruleset 禁止覆盖、移动或删除 `v*.*.*`。默认 `GITHUB_TOKEN` 不得用于创建
发布 Tag。Tag 注释必须包含冻结快照 URI、快照 SHA-256 和 Spec SHA-256。

## 3. 候选与 Compose

Tag 自动触发候选流水线。签名前必须重新下载冻结快照，逐字节验证 Tag、版本、
Git SHA 和 Spec SHA-256。候选成功后归档 Schema 2
`release-manifest.json` 和 `candidate-result.json`。

Compose 部署工作流只接收版本与受保护 Environment；manifest、运行地址和部署
参数均从冻结快照及候选结果解析。部署必须先完成一次性数据库初始化，再启动
API/Web，并核对健康、发布身份、最新迁移、镜像 digest 与远程客户端制品。

## 4. Go/No-Go

以下任一情况均为 NO-GO：输入未批准或过期、出现明文 Secret、Tag 已存在、
main SHA 或任一哈希不一致、S3 版本控制/Object Lock 未启用、签名身份不符、
数据库初始化失败、运行镜像或迁移与清单不一致。

`v1.0.0` 一旦创建即视为已消费。若失败后需要修改代码或冻结输入，必须更新
Desktop/Android 版本并创建 `v1.0.1`；不得删除后重打 `v1.0.0`。
