### Task 10: SpaceSqlIntegrationTests 真库化(环境变量门控)

**Files:**
- Modify: `CP6.Tests\SpaceSqlIntegrationTests.cs`(全文重写 4 测试)
- Create: `CP6.Tests\Infra\SqlServerFactAttribute.cs`

**Interfaces:**
- Produces: `SqlServerFactAttribute : FactAttribute`——ctor 中 `if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CP6_TEST_SQLSERVER"))) Skip = "设 CP6_TEST_SQLSERVER=<连接串> 以运行真库集成测试";`。

**要点:** 4 测试改 `[SqlServerFact]`,连接串来自环境变量,**每测试类一个唯一名临时库**(`CP6Test_{Guid:N}`),`EnsureCreated` 建 schema,`finally EnsureDeleted`。四测试按类头注释(`:7-17`)语义真实断言:①同非空码二插抛唯一索引冲突 ②双 NULL 码共存 ③两阶段换码(经 NULL 中转)成功 ④RowVersion 并发第二写抛 `DbUpdateConcurrencyException`。无环境变量时 Skip(CI 恒绿);本机验证用 `CP6_TEST_SQLSERVER="Server=localhost,1433;Database=master;User Id=sa;Password=<从 C:\CP6\.env 的 MSSQL_SA_PASSWORD 读>;TrustServerCertificate=True"`(实跑一次证明 4 绿,报告贴输出)。

- [ ] **Step 1: 写 SqlServerFactAttribute + 重写 4 测试**
- [ ] **Step 2: 无环境变量跑→4 Skip;设变量跑→4 绿(两种输出都贴报告)**
- [ ] **Step 3: 全量后端绿(默认路径 Skip,基线不降)**
- [ ] **Step 4: Commit + push**(`test(space): 波5 SQL集成测试真库化——CP6_TEST_SQLSERVER门控,过滤唯一索引/两阶段换码/RowVersion并发首获真覆盖`)

---

## 波终验收(主控执行,非任务)

1. fable 终审(whole-branch review)→ 修复 → Ready。
2. 合并 main(--no-ff)+ push,重建 cp6-api 镜像部署(宿主 `dotnet publish -o publish-docker` → 删 appsettings.Local/Development → `docker build` 薄 Dockerfile → `compose up -d cp6-api`),cp6-web 同步重建(本波有前端)。
3. 线上冒烟:①对账 worker 启动日志 ②UpdateSite 改锚 400/E-SPACE-406 ③locate 裸码端点 ja 译文 ④删停用位→T_WmsBin 墓碑行消失→同码再发布成功 ⑤rehome/H4 republish 真库冒烟(波1.5 遗留) ⑥浏览器视觉走查(900 段菜单/三生命周期页/编辑器新面板/Zone 弹窗 ghost 矩形)。
4. 台账+票据落档(平台票不动:CpFormDialog 二次 toast/403 信封中文/总纲 §16.3 错误码表同步)。
