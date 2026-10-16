using System.Text.Json;
using CP6.Core.Services.Pub;
using CP6.Entity.DomainModels.Pub;

namespace CP6.Tests;

public class CodeGenPreviewTests
{
    internal static (GenTable Table, List<GenColumn> Columns) Input() => (new()
    {
        EntityName = "Demo", Module = "Erp", TableName = "Erp_Demo",
        ResourceKey = "demo", RoutePath = "api/erp/demo"
    }, [new() { Name = "Name", ClrType = "string", Label = "名称", Sort = 1 }]);

    [Fact]
    public void Stable_preview_binds_rendered_input_and_preserves_metadata_and_legacy_dictionary()
    {
        var (table, columns) = Input();
        var before = JsonSerializer.Serialize(new { table, columns });
        var service = new CodeGenService();
        var first = service.GeneratePreview(table, columns);
        var again = service.GeneratePreview(table, columns);
        Assert.Equal(first.InputDigest, again.InputDigest);
        Assert.Equal(first.Outputs, again.Outputs);
        Assert.False(first.Applied);
        Assert.Equal("1.0", first.SchemaVersion);
        Assert.Equal(CodeGenService.PreviewTemplateRef, first.TemplateRef);
        Assert.Equal(CodeGenService.PreviewTemplateVersion, first.TemplateVersion);
        Assert.Matches("^sha256:[0-9a-f]{64}$", first.InputDigest);
        Assert.Equal(service.Generate(table, columns), first.Outputs.ToDictionary(x => x.FileName, x => x.Content));
        Assert.Equal(before, JsonSerializer.Serialize(new { table, columns }));
        table.Id = Guid.NewGuid(); table.CreateDate = DateTime.UtcNow;
        Assert.Equal(first.InputDigest, service.GeneratePreview(table, columns).InputDigest);
        columns[0].Label = "新名称";
        var changed = service.GeneratePreview(table, columns);
        Assert.NotEqual(first.InputDigest, changed.InputDigest);
        Assert.NotEqual(first.Outputs[0].Content, changed.Outputs[0].Content);
    }

    [Theory]
    [InlineData("../Demo")]
    [InlineData("Demo;class Injected{}")]
    [InlineData("{{7*7}}")]
    [InlineData("class")]
    [InlineData("CON")]
    [InlineData("BaseEntity")]
    public void Rejects_unsafe_entity_names(string name)
    {
        var (t, c) = Input(); t.EntityName = name;
        Assert.Throws<CodeGenPreviewValidationException>(() => new CodeGenService().Generate(t, c));
    }

    [Theory]
    [InlineData("System.IO.FileInfo")]
    [InlineData("string; public int Injected")]
    [InlineData("{{7*7}}")]
    public void Rejects_non_whitelisted_types(string type)
    {
        var (t, c) = Input(); c[0].ClrType = type;
        Assert.Throws<CodeGenPreviewValidationException>(() => new CodeGenService().Generate(t, c));
    }

    [Fact]
    public void Rejects_duplicates_base_members_null_and_bounded_input_overflow()
    {
        var service = new CodeGenService(); var (t, c) = Input();
        c.Add(new() { Name = "name" });
        Assert.Throws<CodeGenPreviewValidationException>(() => service.Generate(t, c));
        c.RemoveAt(1); c[0].Name = "DeptId";
        Assert.Throws<CodeGenPreviewValidationException>(() => service.Generate(t, c));
        c[0].Name = "Demo";
        Assert.Throws<CodeGenPreviewValidationException>(() => service.Generate(t, c));
        Assert.Throws<CodeGenPreviewValidationException>(() => service.Generate(null!, c));
        Assert.Throws<CodeGenPreviewValidationException>(() => service.Generate(t, null!));
        Assert.Throws<CodeGenPreviewValidationException>(() => service.Generate(t, [null!]));
        Assert.Throws<CodeGenPreviewValidationException>(() => service.Generate(t, Enumerable.Range(0, 513).Select(i => new GenColumn { Name = $"N{i}" }).ToList()));
        t.EntityName = new string('A', 81);
        Assert.Throws<CodeGenPreviewValidationException>(() => service.Generate(t, []));
    }

    [Theory]
    [InlineData("../demo")]
    [InlineData("api/../demo")]
    [InlineData("C:\\demo")]
    [InlineData("api/{{template}}")]
    [InlineData("api/%2e%2e/demo")]
    public void Rejects_path_and_route_template_injection(string route)
    {
        var (t, c) = Input(); t.RoutePath = route;
        Assert.Throws<CodeGenPreviewValidationException>(() => new CodeGenService().Generate(t, c));
    }

    [Fact]
    public void Escapes_free_text_in_each_language_without_evaluating_templates()
    {
        var (t, c) = Input();
        t.TableName = "表\"\n[Injected]"; t.ResourceKey = "权限\"\\\n";
        c[0].Label = "名称'\\\n</summary>\u2028<script>{{7*7}}";
        var files = new CodeGenService().Generate(t, c);
        Assert.Contains("\\u0022\\n[Injected]", files["Demo.cs"]);
        Assert.DoesNotContain("\n[Injected]", files["Demo.cs"]);
        Assert.Contains("&lt;/summary&gt;", files["Demo.cs"]);
        Assert.Contains("&#xA;", files["Demo.cs"]);
        Assert.DoesNotContain("\n</summary>", files["Demo.cs"]);
        Assert.Contains("\\'\\\\\\n", files["demo.ts"]);
        Assert.Contains("\\u2028\\u003cscript\\u003e{{7*7}}", files["demo.ts"]);
        Assert.DoesNotContain("<script>", files["demo.ts"]);
        Assert.Contains("{{7*7}}", files["Demo.cs"]);
    }

    [Fact]
    public void Accepts_unicode_contextual_names_and_supported_scalar_variants()
    {
        var (t, c) = Input(); t.EntityName = "订单";
        c = [new() { Name = "value", ClrType = "decimal" }, new() { Name = "DateTime", ClrType = "DateTime?" },
            new() { Name = "数量", ClrType = "long?" }, new() { Name = "async", ClrType = "string?" }];
        var files = new CodeGenService().Generate(t, c);
        Assert.Contains("public decimal value", files["订单.cs"]);
        Assert.Contains("public DateTime? DateTime", files["订单.cs"]);
        Assert.All(files.Keys, name => Assert.Equal(name, Path.GetFileName(name)));
    }

    [Theory]
    [InlineData("ColumnConfig")]
    [InlineData("VolTable")]
    public void Frontend_import_names_are_legal_entity_and_module_names(string name)
    {
        var (t, c) = Input(); t.EntityName = name; t.Module = name;
        var files = new CodeGenService().Generate(t, c);
        Assert.Contains($"class {name} : BaseEntity", files[$"{name}.cs"]);
        Assert.Contains($"namespace CP6.Entity.DomainModels.{name};", files[$"{name}.cs"]);
        Assert.Contains($"import {{ {char.ToLowerInvariant(name[0]) + name[1..]}Api }}", files[$"{name}View.vue"]);
    }
}
