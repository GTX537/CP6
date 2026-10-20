using System.Text.Json.Nodes;
using CP6.Platform.Messaging;

namespace CP6.Tests;

/// <summary>A previous consumer's unchanged contract assets, not a second source checkout.</summary>
internal sealed class LegacyIdentityBundle : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "cp6-legacy-contract-" + Guid.NewGuid().ToString("N"));
    public Cp6ContractBundle Bundle { get; }
    public LegacyIdentityBundle()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "contracts/events/platform");
        var index = JsonNode.Parse(File.ReadAllBytes(Path.Combine(source, "contract-bundle.v1.json")))!;
        var entries = index["entries"]!.AsArray();
        foreach (var entry in entries.Where(x => !x!["eventType"]!.GetValue<string>().EndsWith(".v1", StringComparison.Ordinal)).ToArray())
            entries.Remove(entry);
        index["bundleVersion"] = "1.0.0";
        Directory.CreateDirectory(root);
        foreach (var entry in entries)
        {
            Copy(entry!["schemaPath"]!.GetValue<string>());
            foreach (var example in entry["examples"]!.AsArray()) Copy(example!["path"]!.GetValue<string>());
        }
        File.WriteAllText(Path.Combine(root, "contract-bundle.v1.json"), index.ToJsonString());
        Bundle = Cp6ContractBundle.Load(root);
        void Copy(string relative)
        {
            var target = Path.GetFullPath(Path.Combine(root, relative));
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Contract path escapes fixture.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(source, relative), target);
        }
    }
    public void Dispose()
    {
        var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), Path.GetFileName(root)));
        if (Path.GetFullPath(root) != expected || !Path.GetFileName(root).StartsWith("cp6-legacy-contract-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected fixture cleanup path.");
        Directory.Delete(root, recursive: true);
    }
}
