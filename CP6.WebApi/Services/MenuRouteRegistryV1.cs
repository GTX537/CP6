using System.Collections.Frozen;
using System.Text.Json;

namespace CP6.WebApi.Services;

internal static class MenuRouteRegistryV1
{
    internal static IReadOnlySet<string> Paths { get; } = LoadPaths();

    private static IReadOnlySet<string> LoadPaths()
    {
        using var source = typeof(MenuRouteRegistryV1).Assembly.GetManifestResourceStream("CP6.Workspace.MenuRoutes")
            ?? throw new InvalidOperationException("缺少已登记的工作台菜单路由。");
        var paths = JsonSerializer.Deserialize<string[]>(source);
        if (paths is not { Length: > 0 } || paths.Any(path => path.Length < 2 || path[0] != '/' ||
            path[1] == '/' || path.Skip(1).Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '/' and not '-' and not '_')))
            throw new InvalidOperationException("工作台菜单路由登记无效。");
        return paths.ToFrozenSet(StringComparer.Ordinal);
    }
}
