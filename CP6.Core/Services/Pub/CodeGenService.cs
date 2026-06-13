using System.Text.RegularExpressions;
using CP6.Entity.DomainModels.Pub;
using Scriban;

namespace CP6.Core.Services.Pub;

/// <summary>
/// 代码生成器 —— PUB 章08 §3/§4/§5。GenTable + GenColumn → Scriban 渲染多类产物，
/// 产物开箱装配 PUB 四粒度（Controller 含常量特性 / Service 继承 BaseCrudService / Entity 实现 IDataScoped）。
/// 二次生成保护：保留 <c>// &lt;custom&gt; ... // &lt;/custom&gt;</c> 区块内的手写代码。
/// </summary>
public class CodeGenService
{
    /// <summary>渲染全部产物：文件名 → 内容。</summary>
    public Dictionary<string, string> Generate(GenTable t, List<GenColumn> cols)
    {
        var model = new
        {
            t.EntityName,
            t.Module,
            t.TableName,
            t.ResourceKey,
            SeqBizKey = t.SeqBizKey,
            CodeField = t.CodeField,
            t.RoutePath,
            EntityLower = Camel(t.EntityName),
            Columns = cols.OrderBy(c => c.Sort).Select(c => new
            {
                c.Name,
                c.ClrType,
                c.Label,
                c.Required,
                c.InList,
                c.InForm,
                JsonName = Camel(c.Name),
                IsString = c.ClrType == "string"
            }).ToList()
        };

        return new Dictionary<string, string>
        {
            [$"{t.EntityName}.cs"] = Render(EntityTpl, model),
            [$"{t.EntityName}Service.cs"] = Render(ServiceTpl, model),
            [$"{t.EntityName}Controller.cs"] = Render(ControllerTpl, model),
            [$"{model.EntityLower}.ts"] = Render(ColumnsTpl, model),
            [$"{t.EntityName}View.vue"] = Render(ViewTpl, model),
        };
    }

    /// <summary>二次生成：用旧文件 // &lt;custom&gt; 区块覆盖新文件对应区块（保护手写代码）。</summary>
    public static string MergeCustomBlocks(string oldContent, string newContent)
    {
        var rx = new Regex(@"// <custom>.*?// </custom>", RegexOptions.Singleline);
        var oldBlocks = rx.Matches(oldContent).Select(m => m.Value).ToList();
        if (oldBlocks.Count == 0) return newContent;
        int i = 0;
        return rx.Replace(newContent, _ => i < oldBlocks.Count ? oldBlocks[i++] : _.ToString()!);
    }

    private static string Render(string tpl, object model) => Template.Parse(tpl).Render(model, m => m.Name);

    private static string Camel(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s[1..];

    // ───── 嵌入式模板 ─────

    private const string EntityTpl = """
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;
        using CP6.Entity;

        namespace CP6.Entity.DomainModels.{{ Module }};

        [Table("{{ TableName }}")]
        public class {{ EntityName }} : BaseEntity, IDataScoped
        {
            /// <summary>归属部门（数据权限）</summary>
            public Guid? DeptId { get; set; }
        {{~ for c in Columns ~}}
            /// <summary>{{ c.Label }}</summary>
            public {{ c.ClrType }} {{ c.Name }} { get; set; }{{ if c.IsString }} = "";{{ end }}
        {{~ end ~}}
        }
        """;

    private const string ServiceTpl = """
        using CP6.Core.EFDbContext;
        using CP6.Core.Services.Pub;
        using CP6.Core.Services.Sys;
        using CP6.Entity.DomainModels.{{ Module }};

        namespace CP6.Core.Services.{{ Module }};

        public class {{ EntityName }}Service : BaseCrudService<{{ EntityName }}>
        {
            public {{ EntityName }}Service(CP6Context db, IDataScopeFilter scope, IFieldPermService fieldPerm,
                ICurrentPermissionContext current, ISeqService seq) : base(db, scope, fieldPerm, current, seq) { }

            protected override string ResourceKey => "{{ ResourceKey }}";
        {{~ if SeqBizKey ~}}
            protected override string? SeqBizKey => "{{ SeqBizKey }}";
        {{~ end ~}}
        {{~ if CodeField ~}}
            protected override string? CodeField => "{{ CodeField }}";
        {{~ end ~}}

            // <custom>
            // 自定义业务方法写在此区块内，二次生成时保留
            // </custom>
        }
        """;

    private const string ControllerTpl = """
        using CP6.Core.Auth;
        using CP6.Core.Pub;
        using CP6.Core.Services.{{ Module }};
        using CP6.Entity.DomainModels.{{ Module }};
        using Microsoft.AspNetCore.Authorization;
        using Microsoft.AspNetCore.Mvc;

        namespace CP6.WebApi.Controllers.{{ Module }};

        [ApiController]
        [Route("{{ RoutePath }}")]
        [Authorize]
        public class {{ EntityName }}Controller : BaseCrudController<{{ EntityName }}, {{ EntityName }}Service>
        {
            public {{ EntityName }}Controller({{ EntityName }}Service svc) : base(svc) { }

            [HttpGet]
            [FieldMask("{{ ResourceKey }}")]
            public override Task<IActionResult> Query(int page = 1, int pageSize = 10) => base.Query(page, pageSize);

            [HttpPost]
            [RequirePermission("{{ ResourceKey }}", "add")]
            public override Task<IActionResult> Add([FromBody] {{ EntityName }} entity) => base.Add(entity);

            [HttpPut]
            [RequirePermission("{{ ResourceKey }}", "edit")]
            public override Task<IActionResult> Edit([FromBody] {{ EntityName }} entity) => base.Edit(entity);

            [HttpDelete]
            [RequirePermission("{{ ResourceKey }}", "delete")]
            public override Task<IActionResult> Del([FromBody] System.Guid[] ids) => base.Del(ids);
        }
        """;

    private const string ColumnsTpl = """
        // 自动生成 {{ EntityName }} 列配置
        import type { ColumnConfig } from '@/components/VolTable.vue'

        export const {{ EntityLower }}Columns: ColumnConfig[] = [
        {{~ for c in Columns ~}}
        {{~ if c.InList ~}}
          { prop: '{{ c.JsonName }}', label: '{{ c.Label }}'{{ if c.Required }}, required: true{{ end }} },
        {{~ end ~}}
        {{~ end ~}}
        ]
        """;

    private const string ViewTpl = """
        <template>
          <div>
            <VolTable :columns="columns" :api="{{ EntityLower }}Api" />
          </div>
        </template>

        <script setup lang="ts">
        import VolTable from '@/components/VolTable.vue'
        import { {{ EntityLower }}Columns as columns } from '@/types/{{ Module }}/{{ EntityLower }}'
        import { {{ EntityLower }}Api } from '@/api/{{ Module }}/{{ EntityLower }}'
        </script>
        """;
}
