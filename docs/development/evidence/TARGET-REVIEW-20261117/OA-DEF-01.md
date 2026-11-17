# OA-DEF-01 目标核对

结论：**目标 DONE**。覆盖卡 WP12-01、WP12-02 已完整 DONE；27 条原 AC 均有适用实际证据，必需修订运行场景为 0 条。此次只核对既有执行、当前适用性与远端采用，没有新运行产品测试。

原 AC 定义取自 engineering-oa.json 中 OA-DEF-01 的准确原稿引用，未改断言。W279 当时遗留项由 W284/W285/W286 和 BUG736 后续交付补齐；W288/W295 为整卡范围核销，不能单独代替下表运行证据。

| 原 AC | 当前核对与实际证据 | 结果 |
|---|---|---|
| AC-D01-01：AC-D01-01：合法新建返回一个head/一个Draft/一receipt，默认disabled，原Key稳定；NOT_RUN | 新 head/draft 默认停用；同事务原命令、审计与回执；[W269](../W269-01/DELIVERY.md)、[W270](../W270-01/DELIVERY.md) | PASS（复用） |
| AC-D01-02：AC-D01-02：同command双击只一个draft，异body冲突且head无第二写；NOT_RUN | 同命令原结果；异输入冲突无新效果；[W269](../W269-01/DELIVERY.md)、[W270](../W270-01/DELIVERY.md)、[W271](../W271-01/DELIVERY.md) | PASS（复用） |
| AC-D01-03：AC-D01-03：两个作者相同旧rowVersion，最多一个保存成功，失败者内容未覆盖；NOT_RUN | 两名真实作者、独立 PG 连接，同 token 一胜一冲突；[W286](../W286-01/DELIVERY.md) | PASS（复用） |
| AC-D01-04：AC-D01-04：clone指定published版本后清身份码、无任务/外呼/通知产生；NOT_RUN | 准确旧发布版克隆，目标新身份且停用，不复制实例历史；[W271](../W271-01/DELIVERY.md) | PASS（复用） |
| AC-D01-05：AC-D01-05：无效图、子流程环/超深度、未注册action的诊断与发布拒绝可定位node/edge；NOT_RUN | 原图预演及致命错误准确节点/边定位，合法未完成草稿保留；[W279](../W279-01/DELIVERY.md)、[W284](../W284-01/DELIVERY.md) | PASS（复用） |
| AC-D01-06：AC-D01-06：预演受资源权限限制、输出仅预测，不能改变实例/业务状态；NOT_RUN | 资源/权限预测为只读解释；零业务效果；[W279](../W279-01/DELIVERY.md)、[W284](../W284-01/DELIVERY.md) | PASS（复用） |
| AC-D02-01：AC-D02-01：字段及table完整往返，布局/默认值/原扩展不因后端投影丢失；NOT_RUN | 完整 schema/表格/布局/默认值/扩展往返，原数字词元保留；[W268](../W268-01/DELIVERY.md)、[W269](../W269-01/DELIVERY.md)、[W284](../W284-01/DELIVERY.md) | PASS（复用） |
| AC-D02-02：AC-D02-02：同名字段、嵌套复杂子表、超200行能力声明拒绝并返回字段路径；NOT_RUN | 重复字段、嵌套表格、超过 200 条等错误保准确路径；[W284](../W284-01/DELIVERY.md) | PASS（复用） |
| AC-D02-03：AC-D02-03：compute按规则顺序以后端结果为准，禁用控件不越字段授权；NOT_RUN | 按序规则后端复算；disabled 不是权限；[W284](../W284-01/DELIVERY.md) | PASS（复用） |
| AC-D02-04：AC-D02-04：更改published字段产生新draft，旧FormData仍用原schema显示；NOT_RUN | 新草稿不重解释旧数据；legacy 显式来源映射与准确原 schema；[W270](../W270-01/DELIVERY.md)、[W274](../W274-01/DELIVERY.md)、[W318](../W318-01/DELIVERY.md) | PASS（复用） |
| AC-D02-05：AC-D02-05：文件源越权拒绝，隐藏字段无下载Ref泄露；NOT_RUN | 真实 FORM 文件绑定/当前权限；隐藏与无权不发 downloadRef；[W285](../W285-01/DELIVERY.md) | PASS（复用） |
| AC-D03-01：AC-D03-01：GET draft连续两次数据库无写；显式创建draft才产生一版；NOT_RUN | GET 纯读，不暗建草稿；旧入口不能旁路；[W269](../W269-01/DELIVERY.md)、[W270](../W270-01/DELIVERY.md)、[W286](../W286-01/DELIVERY.md) | PASS（复用） |
| AC-D03-02：AC-D03-02：相同publish command只一published version/依赖集/receipt；NOT_RUN | 准确预览发布一版；依赖 pin、回执和审计同事务；[W270](../W270-01/DELIVERY.md)、[W272](../W272-01/DELIVERY.md) | PASS（复用） |
| AC-D03-03：AC-D03-03：缺row token、过期预览或改变依赖返回冲突，无部分Published；发布与撤权writer在同WF_AUTH fence串行裁决，门缺失拒新写；NOT_RUN | 旧 token/预览/依赖及最终 WF_AUTH 撤权/到期原子拒绝；[W270](../W270-01/DELIVERY.md)、[W272](../W272-01/DELIVERY.md)、[W294](../W294-01/DELIVERY.md) | PASS（复用） |
| AC-D03-04：AC-D03-04：绕旧helper/旧designer入口不得绕过统一发布规则，旧caller未适配被精确fence；NOT_RUN | 旧 helper/设计器写口已处置；真实 Chrome 使用新 Owner；[W286](../W286-01/DELIVERY.md) | PASS（复用） |
| AC-D03-05：AC-D03-05：发布成功不自动发起业务审批、不自动启用、更不改变旧实例pin；NOT_RUN | 发布不启动、不启用、不改旧 activation/实例 pin；[W270](../W270-01/DELIVERY.md)、[W272](../W272-01/DELIVERY.md)、[W274](../W274-01/DELIVERY.md)、[W294](../W294-01/DELIVERY.md) | PASS（复用） |
| AC-D03-06：AC-D03-06：事务提交后断线可从原command恢复，不误建下一版；NOT_RUN | COMMIT 未知按原键恢复；UI 未知后 403/409 保留原意图；[W270](../W270-01/DELIVERY.md)、[W272](../W272-01/DELIVERY.md)、[W294](../W294-01/DELIVERY.md)、[BUG736](../BUG-736/DELIVERY.md) | PASS（复用） |
| AC-D04-01：AC-D04-01：直接修改/删除published内容被拒，历史字段、摘要、身份不变；NOT_RUN | 已发布版本不可变，建新稿不覆盖原版；[W270](../W270-01/DELIVERY.md)、[W272](../W272-01/DELIVERY.md) | PASS（复用） |
| AC-D04-02：AC-D04-02：新版本发布后在途实例及子流程仍按原依赖版本运行；NOT_RUN | 运行根/子实例采用固定版本；真实页面到 PG 链；[W274](../W274-01/DELIVERY.md)、[W275](../W275-01/DELIVERY.md)、[W276](../W276-01/DELIVERY.md)、[W278](../W278-01/DELIVERY.md) | PASS（复用） |
| AC-D04-03：AC-D04-03：同定义并发创建下一draft只有一个，版本不重复；NOT_RUN | 双作者争下一稿只有一版号，原命令按作者分槽；[W286](../W286-01/DELIVERY.md) | PASS（复用） |
| AC-D04-04：AC-D04-04：legacy current-head重建有明确来源标识，不充当当时发布证据；NOT_RUN | 旧 head 重建和映射显式标 UNVERIFIED_LEGACY，不冒认证历史；[W286](../W286-01/DELIVERY.md)、[W318](../W318-01/DELIVERY.md) | PASS（复用） |
| AC-D04-05：AC-D04-05：旧版本复制为新draft保持来源，恢复不覆盖原版本；NOT_RUN | 指定旧发布版到新稿，源版本保持不变；[W271](../W271-01/DELIVERY.md)、[W286](../W286-01/DELIVERY.md) | PASS（复用） |
| AC-D05-01：AC-D05-01：同一form并发启用两个flow只有一个合法Active，另一409；NOT_RUN | 唯一 active form-flow 绑定；两 PG 连接竞争和改绑回滚；[W273](../W273-01/DELIVERY.md) | PASS（复用） |
| AC-D05-02：AC-D05-02：停用后新submit拒绝，旧Running实例及历史仍保持pin且未自动取消；NOT_RUN | 停用仅阻止新根，已有根/子任务保原 pin；[W273](../W273-01/DELIVERY.md)、[W274](../W274-01/DELIVERY.md)、[W275](../W275-01/DELIVERY.md)、[W276](../W276-01/DELIVERY.md) | PASS（复用） |
| AC-D05-03：AC-D05-03：无published版本/依赖未满足不可Enable，不因head.Enable已有true放行；NOT_RUN | 缺发布版或准确依赖拒绝启用；[W272](../W272-01/DELIVERY.md)、[W273](../W273-01/DELIVERY.md) | PASS（复用） |
| AC-D05-04：AC-D05-04：相同enable command重放不追加重复激活历史，旧token不能覆盖后来停用；NOT_RUN | 同启用命令不重复；旧 token 冲突无暗改绑；[W273](../W273-01/DELIVERY.md) | PASS（复用） |
| AC-D05-05：AC-D05-05：FE冲突后展示真实状态而非保乐观开关；NOT_RUN | 页面真实状态/冲突反馈及原意图恢复；组件和浏览器证据分列；[W278](../W278-01/DELIVERY.md)、[W286](../W286-01/DELIVERY.md)、[BUG736](../BUG-736/DELIVERY.md) | PASS（复用） |

## 远端采用及当前适用性

本次已 fetch 并核对 Main origin/main `1a1f2021c8975bd3555d579dd7312eb9d67fdfcd`。下列源码及采用提交均为该远端祖先；不是仅查看本地分支或运输 ACK。

| 原交付 | 准确源码 | 原采用提交 | PR |
|---|---|---|---|
| [W268](../W268-01/DELIVERY.md) | `6355c84d6ae6c986ed623cab628a289d95c6bf91` | `feed7f6a4374a50233c8377e72b3972ec66982d9` | [PR](https://github.com/GTX537/CP6/pull/696) |
| [W269](../W269-01/DELIVERY.md) | `5cde29b1653f84e344211c03a258e00f2fd9ca2a` | `782ec746f78031059ec50b4987a48e325427f202` | [PR](https://github.com/GTX537/CP6/pull/697) |
| [W270](../W270-01/DELIVERY.md) | `0b2a21d42a8a085ad62a9ee2d320842e18218290` | `ea4236450ade3e0fa5ffe98e1e5aa04aaeec3766` | [PR](https://github.com/GTX537/CP6/pull/698) |
| [W271](../W271-01/DELIVERY.md) | `7751db2fe60f245b32b77ed84b2b12b3da550780` | `09ce18b5a829877509763418d7d2162ad06035d0` | [PR](https://github.com/GTX537/CP6/pull/699) |
| [W272](../W272-01/DELIVERY.md) | `f11a66146dc1d544bb99c3361f9a457eae226454` | `42b4afafa87632060d49978c87ab385beb5c4382` | [PR](https://github.com/GTX537/CP6/pull/700) |
| [W273](../W273-01/DELIVERY.md) | `0d4cd5b4c5f39efadf5f56b384d8df8939883f92` | `32103247c36e3f44bef82494d550e7c98a1878f8` | [PR](https://github.com/GTX537/CP6/pull/701) |
| [W274](../W274-01/DELIVERY.md) | `bf7bf9d3dfe5ccb83a23016ff0f555a2094a5e77` | `40ebdcabc903895bbc6070fd79919b39a086a291` | [PR](https://github.com/GTX537/CP6/pull/704) |
| [W275](../W275-01/DELIVERY.md) | `5e2eb551ae74f44b342b8b7ce165ba4a610a8966` | `ee236f554ebde05203bed63b1baaebab41834c35` | [PR](https://github.com/GTX537/CP6/pull/705) |
| [W276](../W276-01/DELIVERY.md) | `4de8dd763e70265f2098e5d59a0d057662e15249` | `69909b2ec52a56917a41770279072e3384bcd97b` | [PR](https://github.com/GTX537/CP6/pull/706) |
| [W278](../W278-01/DELIVERY.md) | `b445a2c672253ff11025106aca97fb30645ea520` | `f750b0b7e0e0da2108a549ce094ba7c375953085` | [PR](https://github.com/GTX537/CP6/pull/712) |
| [W279](../W279-01/DELIVERY.md) | `f02d43b84720f77cedc32cfa190d1a39e1b1ec21` | `53b0aca0a38cfc7b2d2947ce377dce30e2605b10` | [PR](https://github.com/GTX537/CP6/pull/713) |
| [W284](../W284-01/DELIVERY.md) | `06b1b458889213f939025f783b48bf461e7b385a` | `094627927cb50e60f896a76d1d9a35216119020f` | [PR](https://github.com/GTX537/CP6/pull/720) |
| [W285](../W285-01/DELIVERY.md) | `cd2a12364c7837ff0f916ab2fbc1bea5be636ac3` | `53a805b15f04408547188247e730fd8896b20cb9` | [PR](https://github.com/GTX537/CP6/pull/723) |
| [W286](../W286-01/DELIVERY.md) | `53e0c31522161738b70fab53ad703ff18bb39773` | `26a590985fb562fb6ee489aba2cc91eb67a7be21` | [PR](https://github.com/GTX537/CP6/pull/724) |
| [W294](../W294-01/DELIVERY.md) | `d6176ab865dbdad46a77c4100013e93f41f63009` | `50f4ede693c0619ec88364a89131325d359951f1` | [PR](https://github.com/GTX537/CP6/pull/737) |
| [W318](../W318-01/DELIVERY.md) | `697f77e24a649da25072c6efa4fcf6c7ec11d40f` | `a95fb085f906eda5840060d1ed2d002425ec26db` | [PR](https://github.com/GTX537/CP6/pull/772) |
| [BUG736](../BUG-736/DELIVERY.md) | `11a98c2c641ad56d6fff76d61949b8432a330b52` | `cebf6702661eeba2596463e0534d6069dc447fb9` | [PR](https://github.com/GTX537/CP6/pull/738) |

W295 之后相关定义 Owner/控制器/设计器的产品变更为 W318 的显式 legacy 来源映射及受控复制；已核对其 12 项新 PG、13 项受影响回归（含 2 项浏览器）、16 项组件及类型检查。没有把旧 SQLite 失败或当时未执行项改写为成功。已验证的公共事务/权限/租户门继续复用；审计完整保留清理仍属于 WP02-13b，不在 OA-DEF 27 条原 AC 中冒加或移除。

原27条完整保留；本目标没有剩余缺口。APP 服务节点、个人草稿和其他 OA 目标不连带关闭。无需新的迁移或生产部署。
