using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Core.Services.Pub;

namespace CP6.Tests;

public class CodeGenComparisonTests
{
    private const string EntityPath = "CP6.Entity/DomainModels/Erp/Demo.cs";
    private const string ServicePath = "CP6.Core/Services/Erp/DemoService.cs";
    private static CodeGenComparisonRequest Request(params CodeGenSourceFile[] files)
    {
        var (table, columns) = CodeGenPreviewTests.Input();
        return new(table, columns, CodeGenService.PreviewTemplateRef, CodeGenService.PreviewTemplateVersion, files, []);
    }

    [Fact]
    public void Missing_empty_unchanged_and_explicit_overwrite_have_distinct_meanings()
    {
        var generator = new CodeGenService(); var input = Request();
        var before = JsonSerializer.Serialize(input);
        var missing = generator.Compare(input);
        Assert.All(missing.Outputs, x => { Assert.Equal("NoBaseline", x.Status); Assert.Null(x.Difference); });
        Assert.False(missing.Applied);
        var empty = generator.Compare(Request(new CodeGenSourceFile(EntityPath, "")));
        Assert.Equal("OverwriteBlocked", empty.Outputs[0].Status);
        Assert.Empty(empty.Outputs[0].Difference!.RemovedLines);
        var allowed = generator.Compare(Request(new CodeGenSourceFile(EntityPath, "")) with { OverwritePaths = [EntityPath] });
        Assert.Equal("OverwriteAllowed", allowed.Outputs[0].Status);
        Assert.NotEqual(empty.ComparisonDigest, allowed.ComparisonDigest);
        Assert.Equal(empty.InputDigest, allowed.InputDigest);
        var same = generator.Compare(Request(new CodeGenSourceFile(EntityPath, missing.Outputs[0].Content!)));
        Assert.Equal("Unchanged", same.Outputs[0].Status);
        Assert.Empty(same.Outputs[0].Difference!.RemovedLines);
        Assert.Empty(same.Outputs[0].Difference!.AddedLines);
        Assert.Equal(before, JsonSerializer.Serialize(input));
        Assert.Equal(missing.ComparisonDigest, generator.Compare(input).ComparisonDigest);
        var eofBlock = generator.Compare(Request(new CodeGenSourceFile(ServicePath, "// <custom>\rkeep();\r// </custom>")));
        Assert.Contains("// </custom>\n}", eofBlock.Outputs.Single(x => x.TargetPath == ServicePath).Content);
    }

    [Fact]
    public void Custom_code_is_preserved_exactly_and_difference_reconstructs_the_candidate()
    {
        const string block = "    // <custom>\r\n    public string Label => \"手写<&>\";\r\n    // </custom>\r\n";
        var old = "old framework\r" + block + "end without newline";
        var generator = new CodeGenService();
        var result = generator.Compare(Request(new CodeGenSourceFile(ServicePath, old)) with { OverwritePaths = [ServicePath] });
        var file = result.Outputs.Single(x => x.TargetPath == ServicePath);
        Assert.Equal("OverwriteAllowed", file.Status);
        Assert.Equal(1, file.PreservedCustomBlocks);
        Assert.Contains(block, file.Content);
        Assert.DoesNotContain("old framework", file.Content);
        Assert.DoesNotContain("自定义业务方法写在此区块内", file.Content);
        Assert.Equal(file.Content, Apply(old, file.Difference!));
        var unchanged = generator.Compare(Request(new CodeGenSourceFile(ServicePath, file.Content!)));
        Assert.Equal("Unchanged", unchanged.Outputs.Single(x => x.TargetPath == ServicePath).Status);
        var reordered = Request(new CodeGenSourceFile(ServicePath, old), new CodeGenSourceFile(EntityPath, "")) with { OverwritePaths = [ServicePath, EntityPath] };
        Assert.Equal(generator.Compare(reordered).ComparisonDigest,
            generator.Compare(reordered with { ExistingFiles = reordered.ExistingFiles.Reverse().ToArray(), OverwritePaths = [EntityPath, ServicePath] }).ComparisonDigest);
    }

    [Theory]
    [InlineData("old\n// <custom>\nhandwritten")]
    [InlineData("// </custom>\n")]
    [InlineData("// <custom>\n// <custom>\n// </custom>\n// </custom>")]
    [InlineData("// <custom>\none\n// </custom>\n// <custom>\ntwo\n// </custom>\n")]
    public void Damaged_or_unmatched_blocks_block_overwrite_without_silently_dropping_code(string old)
    {
        var result = new CodeGenService().Compare(Request(new CodeGenSourceFile(ServicePath, old)) with { OverwritePaths = [ServicePath] });
        var file = result.Outputs.Single(x => x.TargetPath == ServicePath);
        Assert.Equal("CustomBlockConflict", file.Status);
        Assert.Null(file.Content); Assert.Null(file.Difference); Assert.NotNull(file.Reason);
        var removedSlot = new CodeGenService().Compare(Request(new CodeGenSourceFile(EntityPath, "// <custom>\nkeep\n// </custom>")) with { OverwritePaths = [EntityPath] });
        Assert.Equal("CustomBlockConflict", removedSlot.Outputs[0].Status);
    }

    [Theory]
    [InlineData("../appsettings.json")]
    [InlineData("CP6.Entity/DomainModels/Erp/../Erp/Demo.cs")]
    [InlineData("C:/repo/CP6.Entity/DomainModels/Erp/Demo.cs")]
    [InlineData("CP6.Entity\\DomainModels\\Erp\\Demo.cs")]
    [InlineData("cp6.entity/DomainModels/Erp/Demo.cs")]
    [InlineData("CP6.Entity/DomainModels/Erp/Demo.cs:stream")]
    public void Caller_cannot_redirect_targets_or_whitelist_unknown_paths(string path)
    {
        var generator = new CodeGenService();
        Assert.Throws<CodeGenPreviewValidationException>(() => generator.Compare(Request(new CodeGenSourceFile(path, "original"))));
        Assert.Throws<CodeGenPreviewValidationException>(() => generator.Compare(Request(new CodeGenSourceFile(EntityPath, "")) with { OverwritePaths = [path] }));
    }

    [Fact]
    public void Boundary_rejects_missing_duplicate_oversize_or_unavailable_inputs()
    {
        var generator = new CodeGenService();
        foreach (var input in new CodeGenComparisonRequest?[] {
            null, Request() with { TemplateVersion = "unavailable" }, Request() with { TemplateRef = "file:///template" },
            Request(new CodeGenSourceFile(EntityPath, "a"), new CodeGenSourceFile(EntityPath, "b")), Request() with { OverwritePaths = [EntityPath] },
            Request(new CodeGenSourceFile(EntityPath, "")) with { OverwritePaths = [EntityPath, EntityPath] },
            Request(new CodeGenSourceFile(EntityPath, null!)), Request(new CodeGenSourceFile[] { null! }), Request(new CodeGenSourceFile(EntityPath, "\uD800")),
            Request(new CodeGenSourceFile(EntityPath, new string('汉', CodeGenService.PreviewMaxRequestBytes / 3 + 1))) })
            Assert.Throws<CodeGenPreviewValidationException>(() => generator.Compare(input));
        var unsafeModule = Request(); unsafeModule.Table.Module = "CON";
        Assert.Throws<CodeGenPreviewValidationException>(() => generator.Compare(unsafeModule));
    }

    [Fact]
    public void Large_line_count_and_mixed_line_endings_have_exact_non_truncated_differences()
    {
        var generator = new CodeGenService();
        foreach (var old in new[] { "same\r\nold\nlast", new string('\n', 200_000), "\u0000<source>\r" })
        {
            var result = generator.Compare(Request(new CodeGenSourceFile(EntityPath, old)) with { OverwritePaths = [EntityPath] });
            var output = result.Outputs[0];
            Assert.Equal(output.Content, Apply(old, output.Difference!));
        }
        var candidate = generator.Compare(Request()).Outputs[0].Content!;
        // Retain matching leading/trailing lines rather than reporting the entire file changed.
        var changed = candidate.Replace("class Demo :", "class Previous :", StringComparison.Ordinal);
        var diff = generator.Compare(Request(new CodeGenSourceFile(EntityPath, changed))).Outputs[0].Difference!;
        Assert.True(diff.StartLine > 1); Assert.Single(diff.RemovedLines); Assert.Single(diff.AddedLines);
        Assert.Equal(candidate, Apply(changed, diff));
    }

    private static string Apply(string original, CodeGenDifference difference)
    {
        var lines = Regex.Matches(original, @"[^\r\n]*(?:\r\n|\r|\n|$)").Select(x => x.Value).Where(x => x.Length > 0).ToList();
        Assert.Equal(difference.RemovedLines, lines.Skip(difference.StartLine - 1).Take(difference.RemovedLines.Count));
        lines.RemoveRange(difference.StartLine - 1, difference.RemovedLines.Count);
        lines.InsertRange(difference.StartLine - 1, difference.AddedLines);
        return string.Concat(lines);
    }
}
