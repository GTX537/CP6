# 公开源码清单与复现

本目录仅包含CP6 Core和Platform公开源码；CRM清单保留在[私有完整报告](https://github.com/GTX537/CP6.CRM/blob/main/docs/audits/2026-09-16-full-project/data/README.md)。

| 文件 | 内容与口径 |
| --- | --- |
| [inventory-summary.json](inventory-summary.json) | 固定源码SHA、文件/项目/源码/声明统计及目录分布 |
| [projects.csv](projects.csv) | 58个csproj，含测试、工具和参考项目，不是58个服务 |
| [api-surface.csv](api-surface.csv) | 1,102条HTTP文本声明，不是精确运行endpoint数 |
| [persistence.csv](persistence.csv) | 319处public DbSet声明，含docs参考代码3处，不是物理表数 |
| [ui-files.csv](ui-files.csv) | 318个UI文件，包含组件/布局/XAML资源/参考材料，不是页面数 |
| [ui-page-inventory.csv](ui-page-inventory.csv) | 248个主Web views文件及静态依赖 |
| [ui-route-inventory.csv](ui-route-inventory.csv) | 151动态映射与23静态组件记录 |
| [ui-api-call-sites.csv](ui-api-call-sites.csv) | 925个主Web API包装器HTTP调用行 |
| [ui-inventory-summary.json](ui-inventory-summary.json) | 主Web模块/路由/组件使用统计 |

scope只按目录归类：reference为docs；engineering为tools/eng/scripts/tests；其余application_or_library中也可能有根目录测试项目。源文件行数含空行/注释；声明扫描按命名排除测试/fixture/生成文件，具体正则公开，不能替代编译器或运行验证。

在[主报告](../README.md)的固定SHA独立干净检出上使用Python 3.8+标准库运行：

```powershell
python ./docs/audits/2026-09-16-full-project/inventory.py --core $CoreSnapshot --platform $PlatformSnapshot --output $AuditOutput
python ./docs/audits/2026-09-16-full-project/inventory_ui.py --core $CoreSnapshot --output $UiAuditOutput
```

主扫描器读取Git跟踪文件和磁盘内容，UI辅助脚本枚举src/e2e。原始基线不含本次报告和工具；在文档合并后的main扫描会多出文件。无fetch、安装、构建、测试、HTTP或数据库访问。跨仓私有扫描可显式增加--crm，但不得将其结果未经范围审查提交到公开仓库。
