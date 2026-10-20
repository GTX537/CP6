using System.Collections.ObjectModel;

namespace CP6.Core.Services.Sys;

/// <summary>Startup definitions, captured together. This is not an Owner mapping approval or a permission grant.</summary>
public static class AuthorizationCatalogV1
{
    internal static AuthorizationCatalogStoreV1 Store { get; } = new();
    public static AuthorizationCatalogSnapshotV1 Capture() => Store.Capture();
    // Production hosts seal after registering all definitions, before accepting requests.
    public static void Seal() => Store.Seal();
}

public sealed record AuthorizationScopeDefinitionV1(string Key, string Name, IReadOnlyList<int> Supports, int Default);
public sealed record AuthorizationCatalogSnapshotV1(
    IReadOnlyDictionary<string, IReadOnlyList<FieldRegistry.Field>> Fields,
    IReadOnlyDictionary<string, AuthorizationScopeDefinitionV1> Scopes, bool IsSealed);

internal sealed class AuthorizationCatalogStoreV1
{
    private readonly object sync = new();
    private readonly Dictionary<string, IReadOnlyList<FieldRegistry.Field>> fields = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AuthorizationScopeDefinitionV1> scopes = new(StringComparer.Ordinal);
    private bool sealedDefinitions;
    private AuthorizationCatalogSnapshotV1? snapshot;

    public void RegisterFields(string key, IEnumerable<FieldRegistry.Field> input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(input);
        var copy = input.ToArray();
        if (copy.Any(f => f is null || string.IsNullOrWhiteSpace(f.Name) || f.Label is null)
            || copy.Select(f => f.Name).Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("Invalid or duplicate field definition.", nameof(input));
        Array.Sort(copy, (a, b) => StringComparer.Ordinal.Compare(a.Name, b.Name));
        lock (sync)
        {
            if (fields.TryGetValue(key, out var previous) && previous.SequenceEqual(copy)) return;
            if (sealedDefinitions) throw new InvalidOperationException("IAM_CATALOG_SEALED");
            fields[key] = Array.AsReadOnly(copy);
            snapshot = null;
        }
    }

    public void RegisterScope(string key, string name, IEnumerable<int> input, int defaultScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(input);
        var copy = input.Order().ToArray();
        if (copy.Length == 0 || copy.Any(x => x is < 1 or > 5) || copy.Distinct().Count() != copy.Length || !copy.Contains(defaultScope))
            throw new ArgumentException("Invalid scope domain or default.", nameof(input));
        var value = new AuthorizationScopeDefinitionV1(key, name, Array.AsReadOnly(copy), defaultScope);
        lock (sync)
        {
            if (scopes.TryGetValue(key, out var previous) && previous.Name == name && previous.Default == defaultScope
                && previous.Supports.SequenceEqual(copy)) return;
            if (sealedDefinitions) throw new InvalidOperationException("IAM_CATALOG_SEALED");
            scopes[key] = value;
            snapshot = null;
        }
    }

    public AuthorizationCatalogSnapshotV1 Capture()
    {
        lock (sync)
        {
            // Values and nested lists are immutable; copy both indexes under the same lock.
            return snapshot ??= new(
                new ReadOnlyDictionary<string, IReadOnlyList<FieldRegistry.Field>>(new Dictionary<string, IReadOnlyList<FieldRegistry.Field>>(fields, StringComparer.Ordinal)),
                new ReadOnlyDictionary<string, AuthorizationScopeDefinitionV1>(new Dictionary<string, AuthorizationScopeDefinitionV1>(scopes, StringComparer.Ordinal)),
                sealedDefinitions);
        }
    }

    public void Seal() { lock (sync) { sealedDefinitions = true; snapshot = null; } }
}
