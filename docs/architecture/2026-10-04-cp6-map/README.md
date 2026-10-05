# CP6 中文交互项目地图

打开 [index.html](./index.html)，按整体运行、业务职责、Space、数据库、打包运行、发布推广的顺序查看六张 Archify 架构图。每张 HTML 都是独立文件，可在浏览器直接打开；点击节点上的来源入口查看固定版本的代码，使用搜索、聚焦、缩放和导出操作阅读关系。

这份地图固定解释 CP6 提交 `ebcbc4998faa7fee8e38db861753c6e90bdc100a`。它覆盖 Web、WPF、Android、统一 WebApi、业务域、Space 分层、双库持久化与发布边界。解决方案内的 21 个项目、仓库全部 48 个已跟踪 C# 项目及项目引用保存在 [manifest.json](./manifest.json)，入口页面包含可展开的完整目录清单。源码中的 Platform NuGet 依赖与独立 CRM 对接按实际边界说明；根工作区的未跟踪项目、个人配置与当前进程状态不属于这个固定源码快照。

| 图 | 阅读问题 |
| --- | --- |
| 01 整体运行结构 | 三个客户端怎样共用 API、服务、数据库和文件？ |
| 02 业务职责 | ERP、PUR、PLAN、MES、WMS、FIN、OA/WF、SYS/PUB/Platform 各负责什么？ |
| 03 Space | 设计、上传、版本发布、WMS 适配与运行物化怎样配合？ |
| 04 数据库 | 一次请求怎样到达持久化层？部署时怎样选择 SQL Server 或 PostgreSQL？ |
| 05 打包运行 | 调试、publish、dist、镜像、Compose 与 Kubernetes 有什么关系？ |
| 06 发布推广 | 本地验证、GitHub R2/GHCR、Azure 桥与环境审批是什么关系？ |

图 02 的箭头表示统一后端提供模块服务，不代表所有业务自动依次流转。图 04 中两个数据库出口带 Provider 条件，一次部署固定使用一个引擎。图 03 的发布写入受权限、预检和状态条件约束；运行物化的实际调用见 [`SpacePublishOrchestrator.Execution.cs`](https://github.com/GTX537/CP6/blob/ebcbc4998faa7fee8e38db861753c6e90bdc100a/CP6.Space.Infrastructure/SpacePublishOrchestrator.Execution.cs#L619-L638)。远程 CAD 按启用与批准配置接入，仿真和候选工具不代表现场验收。

本机 PostgreSQL 数据副本及启动方法见 [运行手册](../../devops/CP6DB-POSTGRESQL-LOCAL-COPY.md)。发布规则以 [DevOps 入口](../../devops/README.md) 和 [R2 主规范](../../client/r2/README.md) 为准。图中构建和部署关系来自源码定义，本任务没有执行容器构建或 Kubernetes/生产部署；API Dockerfile 的 restore 前 PostgreSQL 项目复制清单作为待专项验证的静态发现保留。

## 生成与验证

使用 [Archify](https://github.com/tt-a1i/archify) `3.0.1`，工具源码固定为 `594f6087358610bd16e64e5602976020871b6bff`。每张图的 `candidate.json` 是可编辑的图结构，节点带仓库相对路径与行号；图与候选文件均保存在 `.archify/` 下各自的目录。

六张图实际完成 `finalize architecture --repo-root <固定源码检出目录> --quality showcase --json`；结构验证、交付、严格制品检查与 Chrome 浏览器检查全部通过，零诊断。浏览器检查覆盖 1440×900、1600×1000、1920×1080、2048×1320 的桌面尺寸及要求的明暗主题。总览与数据库图另查看了 1440×900 明色、2048×1320 暗色截图，入口页检查了 1440×900 与 390×844，无横向溢出；本地 12 个既有导航目标 HTTP 200。结果和候选/HTML SHA-256 见 [verification.json](./verification.json)。其余四图完成自动浏览器检查，未声称额外人工观图。

原始工具收据包含本机绝对路径，保留在生成工作区并通过本目录 `.gitignore` 排除；公开验证摘要保留真实范围与制品哈希。源码编译、业务回归、现场设备、Docker/K8s 和生产发布不属于这次图生成的验证范围，没有触发远程 Actions。

本目录 `.gitattributes` 对六份生成 HTML 禁用换行转换，确保 Git 字节与已校验制品哈希一致；上游查看器内的空白行保留，并只对这些生成文件排除行尾空白风格诊断。候选 JSON 和验证摘要固定使用 LF，手写文档仍执行正常差异检查。

需要更新时，先确定新的源码版本并逐项核对关系，再修改候选的 `meta.repository.revision` 与源码引用；仅更改标题或 SHA 不会自动刷新地图。在新的输出目录运行固定版本工具的完整 `finalize`，保留失败记录，更新入口、清单和验证摘要。可复用当前的候选形状；已有源码快照与其收据保持可追溯。

生成 HTML 携带 Archify 查看器代码。转载本目录时保留 [MIT 许可](./ARCHIFY-LICENSE.txt) 和 [第三方声明](./ARCHIFY-THIRD-PARTY-NOTICES.md)。
