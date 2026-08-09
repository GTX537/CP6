using CP6.Client.Api;

namespace CP6.Client.Core;

public sealed class ClientOptions
{
    public required Uri ApiBaseAddress { get; set; }
    public required ClientContext Context { get; init; }
    public string Platform { get; init; } = string.Empty;
    public string LanguageDirectory { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CP6", "lang");
}

public interface IRefreshTokenStore
{
    Task<string?> ReadAsync(CancellationToken ct = default);
    Task WriteAsync(string refreshToken, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
}

public interface INavigationService
{
    event EventHandler<string>? RouteChanged;
    string CurrentRoute { get; }
    void Navigate(string route);
}

public sealed class NavigationService : INavigationService
{
    public event EventHandler<string>? RouteChanged;
    public string CurrentRoute { get; private set; } = "login";

    public void Navigate(string route)
    {
        CurrentRoute = route;
        RouteChanged?.Invoke(this, route);
    }
}
