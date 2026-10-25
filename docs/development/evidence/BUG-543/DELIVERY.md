# BUG543 当前身份目录新安装函数准入

[Issue543](https://github.com/GTX537/CP6/issues/543)，P1，COORD本聊天单人。W130实际Main→CRM验证发现；W130-01原分支ebf2a2425a4778c63b6facb38745c367f60a0f5d已完整推送并官方finish deferred。独立codex/bug-543-directory-current从核实main9297eb12cc997f25c5c10271f5abf3f19b1846c5，复用DEV-1；CRM继续同一W130-02/DEV-2，无新增树。

真实新建PG执行当前全部迁移后，身份目录安装器首项固定布局检查拒绝。现行SampleDepartmentOwnerV1覆盖了mapping_validate，InquiryRoleScopesV1增加changed/require_snapshot；旧清单未同步。拒绝按原协议回滚，没有放行未经验证的来源。

核对20261024025500和20261024050000两个已接受迁移的原始定义、规范化CRLF后的prosrc SHA-256与本轮真实PG目录一致，只增加三个精确函数体准入。原Machine映射digest保留；新增Inquiry schema使用同样的trigger返回类型、plpgsql、固定pg_catalog search_path、当前owner、禁止SECURITY DEFINER和禁止额外/PUBLIC EXECUTE检查。普通directory_a/b没有任何业务函数执行权。未改迁移历史、部署配置、既有安装封印或公共线合同。

相关本地验证：Main四项通过，另CRM独立进程原始基线一项通过。来源：D:\CP6-local-test\wave130\foundation\bug543-fixed-20261025T065101Z.json的两项正/负向PG验证；bug543-source-retest-20261025T065156Z中实际配置身份A→B→A一项成功；D:\CP6-local-test\wave130\foundation\bug543-cross-final-20261025T065451Z.json的实际Main HTTP→CRM两进程完整目录/原始1MiB字节/发布回滚与提交一项，子TRX实际一项通过。负向用例覆盖原七类及新增七类询盘函数篡改、未知函数、SECURITY DEFINER、search_path和额外执行权限，不按内部断言数膨胀任务数量。

bug543-red在修复前正向真实失败、负向通过；W130早期失败定位日志及临时仅类型/栈/目录哈希诊断全部保留，诊断代码已移除。一次source运行撞上尚未停止的本轮Main测试PG端口，未执行行为；确认释放后串行复测。首次实际跨仓又暴露新CRM类名被旧模糊过滤器误选，旧基线子用例已通过、新用例缺少对应凭据输入失败；新类名已在W130任务更正，使用全新自有PG保留原来源不可替换边界后复测通过。未把这些失败标为成功。

一次完整作者SPEC/QUALITY审查：精确已审查来源、旧接受范围保留、ACL及未知/危险函数继续拒绝；无密钥、机器配置或调试残留。四项Main与CRM子进程结果按相同输入复用；仅SQL注释随后完善，不重跑。所有Main自有PG已停止，数据/缓存保留。Actions零，工作流/依赖未变。

正常PR远端采用后finish准确分支并释放DEV-1，然后resume原W130分支。Issue使用Refs，待W130实际原生后台C2凭据→目录→PG原步骤也通过后回写并关闭；不以本修复完成全D5或七天目标，不需既有环境或生产验证。最终采用与收尾引用bug543-main-delivery.json及bug543-finish.log，七天ACTIVE。
