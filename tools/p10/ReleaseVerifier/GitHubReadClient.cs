using System.Text.Json;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// Internal transport only. Returned API JSON is input to proof validators, never candidate acceptance.
// No injected endpoint/handler or ambient credential; managed caller-owned token strings cannot be zeroed.
internal sealed class GitHubReadClient : IDisposable
{
    private readonly HttpClient _client;
    private string? _token;

    internal GitHubReadClient(string token)
    {
        GitHubWirePolicy.RequireToken(token);
        _token = token;
        _client = new HttpClient(GitHubWirePolicy.CreateHandler()) { Timeout = Timeout.InfiniteTimeSpan };
    }

    internal async Task<JsonElement> ReadAsync(GitHubReadTarget target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_token is null) throw GitHubWirePolicy.Error("github-client-disposed");
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            using var request = GitHubWirePolicy.Request(target, _token);
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            return GitHubApiJson.Parse(await GitHubWirePolicy.ReadResponseAsync(response, deadline.Token, target));
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw GitHubWirePolicy.Error("github-timeout");
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // No response body, URL, token, runner details or inner exceptions escape this boundary.
            throw GitHubWirePolicy.Error("github-transfer");
        }
    }

    public override string ToString() => "GitHubReadClient";
    public void Dispose()
    {
        _client.Dispose();
        _token = null;
    }
}
