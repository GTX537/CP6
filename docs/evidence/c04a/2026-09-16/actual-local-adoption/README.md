# 原库实际采用：A10/A11完成，A12运行恢复待办

本次操作的是已批准的本机原数据库，RunId `8b969e49-dffe-4c97-b096-1c082cd037fb`。**整体固定清单78.3%，C04A 11/12＝91.7%；正式里程碑仍60%（3/5）。** 相比上一交付75.0%，A11和A10各增加1.6667个百分点，共增加3.3333个百分点。A12最终运行验收未关闭。

## 实际完成

- 管理员回执确认部署Agent Stopped/Disabled，SQL Agent Stopped/Manual；旧根API停止且restart=no，八个Windows旧入口退休、原字节保留。429个批准发布文件与18项宿主输入核对；最终发布字节429/429仍匹配，没有重建。A11闭环。
- 六份当前COPY_ONLY/CHECKSUM备份已VERIFYONLY并保留。此前六库恢复950表、A08/A09和五项SQL证明沿用，不重复测试；不声称共同停写快照。
- 原认证/身份/目录及两个业务库只初始化一次；13张原表的既有内容和原keyring保留。三个实际Core来源Frozen/generation1，60张旧CRM表均空且保留。原账号密码/OIDC、工作区和列表读取成功；整组工具持有目标锁并取得三份实时Frozen证明后，两个目标原子Enabled/generation1。
- 原C02管理员实际上仅有四项销售权限，第一次日历保存403并未写入。补入缺失的静态crm-site菜单目录一行；权限授予通过现有Core API完成，仅为两个既有Role1管理员增加crm-lead:assign及crm-site:query/configure，并把管理范围限定到本人部门与已批准默认部门，其他权限保留。正常权限事件投影生效后继续，没有直接修改投影或绕过认证。
- 两组织各正常保存、预览、发布一次，返回版本/ETag及公开inquiry表单200绑定；没有创建合成Lead。A10闭环。

| 组织 | 实际CalendarVersion | 发布生效UTC |
| --- | --- | --- |
| c02-primary | `8691f3244eb44801981c4ae359138581` | 2026-09-16T10:50:08.2968902Z |
| c02-isolated | `685a4ccfd7b54bbeb4fefd43015347bc` | 2026-09-16T10:51:27.8381783Z |

两个目标已Written。API对已生效Published日历返回Active，SQL物理Status为10、租户列为TenantId。验收脚本对状态/列名的假设已纠正；首次发布200后只补读，没有重复写入。后续只能前向修复，禁止恢复旧备份覆盖现库或重开旧写入口。

## 运行问题与证据边界

首次启用CRM_TARGET_LOCK_TIMEOUT后，两目标仍Closed/generation0。暂停自有宿主后，可用内存602→1710MiB，原请求9.587秒成功；未放宽超时或重跑旧测试。Docker SQL原内存无上限，2GiB上限没有缓解当时压力，最终设置1024MiB，可用内存恢复1485MiB；原生SQL原1024MiB不变。此本机运行配置调整单独记录，不冒充审阅包原配置，也不是生产调优。

两个原Dapr容器曾因Kafka未就绪而退出，按准确身份在Kafka就绪后重启。没有创建新消息环境。

用户再次继续时，Docker/WSL及自有宿主已退出，原因未确定，Windows未重启。Docker恢复遇到两个AF_UNIX残留对象：首个Docker/run目录经用户明确允许后已保留改名；第二个Secrets Engine目录只含零字节engine.sock，Windows连管理员改名也Access denied。未恢复出厂、未删除数据库/镜像/卷、未改ACL或关闭安全机制。当前Docker和宿主未恢复，历史ready文件不能证明仍运行。

中断后[native-post-interruption-state.json](native-post-interruption-state.json)确认两个Written目标、两个Status10日历、两份原生Frozen来源、身份/目录Ready及原密钥均保留。Docker第三来源的最后有效实时证明是[启用回执](verified-target-activation-after-pause.json)，不冒充中断后的新观察。

## 继续路径

1. 让Windows释放第二个残留socket；拟议受控Windows重启尚未获批或执行，先保全工作与交接。
2. 恢复Docker，核对原cp6-db身份/1024MiB配置及旧API stopped/no restart。启动原C02 Kafka并确认就绪，再启动两个原Dapr容器。
3. 按PID和启动时间保留失效进程记录，使用同一批准宿主请求/429文件包恢复；禁止旧入口、重新初始化/冻结/启用/发布。
4. 完成A12来源保护、Written/日历版本、原密钥、五个自有进程、三个原transport、health/live与health/ready及表单读取验收，再完成交付，才计80%。
5. C04B仍须CRM11生产演练/切换、一个正式发布周期的只读观察、旧EF映射及运行时依赖退出和前向清理。本机空来源采用不替代生产验收，也不启动正式生产观察期。

未执行生产部署、Actions或Tag发布。私有HTTP响应、DPAPI会话、完整源检查、备份和日志仅保留本机；此处是白名单无内容报告。manifest绑定原始字节，局部.gitattributes禁止换行转换。各组件回执中的保守false字段保留原值，外部批准/控制由独立证据补足，不改写历史原件。
