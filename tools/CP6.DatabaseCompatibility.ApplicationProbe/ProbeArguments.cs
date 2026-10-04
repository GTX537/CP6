using System.Globalization;
using System.Text.RegularExpressions;
using CP6.Core.Persistence;

namespace CP6.DatabaseCompatibility.ApplicationProbe;

internal sealed record ProbeArguments(
    string Command, DatabaseProvider Provider, string ReportPath, string? StatePath,
    Guid Tenant, Guid User, string? EventKey, string? ExpectedStatus,
    int ExpectedAttempts, int WaitSeconds, int TimeoutSeconds, Uri? BaseUri)
{
    internal static ProbeArguments Parse(string[] args)
    {
        AppProbe.Require(args.Length > 0 && args[0] is
            "histories" or "enqueue-notification" or "notification-status" or "capture" or "verify"
            or "signalr-verification" or "replay-notification", "command-known");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var allowed = new HashSet<string>(["provider", "report", "state", "tenant", "user", "event-key",
            "expect", "expected-attempts", "wait-seconds", "timeout-seconds", "base-uri"], StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i += 2)
        {
            AppProbe.Require(i + 1 < args.Length && args[i].StartsWith("--", StringComparison.Ordinal), "argument-pairs");
            var name = args[i][2..];
            AppProbe.Require(allowed.Contains(name) && values.TryAdd(name, args[i + 1]), "argument-known-unique");
        }
        var provider = Required("provider") switch
        {
            "SqlServer" => DatabaseProvider.SqlServer,
            "PostgreSql" => DatabaseProvider.PostgreSql,
            _ => throw new ProbeFailure("provider-explicit-known")
        };
        var report = Path.GetFullPath(Required("report"));
        var state = values.TryGetValue("state", out var stateText) ? Path.GetFullPath(stateText) : null;
        var notification = args[0] is "enqueue-notification" or "notification-status" or "signalr-verification" or "replay-notification";
        var tenant = notification ? GuidValue("tenant") : Guid.Empty;
        var user = notification ? GuidValue("user") : Guid.Empty;
        var eventKey = notification ? Required("event-key") : null;
        if (notification)
            AppProbe.Require(Regex.IsMatch(eventKey!, @"\Awp6-[a-z0-9-]{1,100}\z", RegexOptions.CultureInvariant), "event-key-wp6-bounded");
        var expected = args[0] == "notification-status" ? Required("expect") : null;
        AppProbe.Require(expected is null or "queued" or "dispatched", "expected-status-known");
        var attempts = Number("expected-attempts", expected == "dispatched" ? 1 : 0, 0, 10);
        var wait = Number("wait-seconds", 0, 0, 60);
        var timeout = Number("timeout-seconds", 300, 10, 600);
        Uri? baseUri = null;
        if (args[0] == "signalr-verification")
        {
            var baseUriText = Required("base-uri");
            AppProbe.Require(Regex.IsMatch(baseUriText, @"\Ahttp://(?:localhost|127\.0\.0\.1|\[::1\]):[0-9]{1,5}/?\z", RegexOptions.CultureInvariant)
                && Uri.TryCreate(baseUriText, UriKind.Absolute, out baseUri)
                && baseUri.Scheme == "http" && baseUri.IdnHost is "localhost" or "127.0.0.1" or "::1"
                && baseUri.Port is >= 1024 and <= 65535 && baseUri.UserInfo.Length == 0
                && baseUri.AbsolutePath == "/" && baseUri.Query.Length == 0 && baseUri.Fragment.Length == 0,
                "signalr-base-uri-loopback");
            AppProbe.Require(timeout >= 60, "signalr-timeout-at-least-sixty-seconds");
        }
        else AppProbe.Require(!values.ContainsKey("base-uri"), "base-uri-only-signalr-command");
        if (args[0] is "capture" or "verify")
        {
            AppProbe.Require(state is not null && !string.Equals(state, report, StringComparison.OrdinalIgnoreCase), "private-state-distinct");
            AppProbe.Require(Path.GetFileName(state!).EndsWith(".private.json", StringComparison.OrdinalIgnoreCase)
                || state!.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Contains("private", StringComparer.OrdinalIgnoreCase), "state-path-private");
            AppProbe.Require(args[0] == "capture" ? !File.Exists(state) : File.Exists(state), "state-file-lifecycle");
        }
        else AppProbe.Require(state is null, "state-only-snapshot-command");
        return new(args[0], provider, report, state, tenant, user, eventKey, expected, attempts, wait, timeout, baseUri);

        string Required(string name)
        {
            AppProbe.Require(values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value), "required-" + name);
            return value!;
        }
        Guid GuidValue(string name)
        {
            AppProbe.Require(Guid.TryParse(Required(name), out var value) && value != Guid.Empty, "guid-" + name);
            return value;
        }
        int Number(string name, int fallback, int min, int max)
        {
            if (!values.TryGetValue(name, out var text)) return fallback;
            AppProbe.Require(int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                && number >= min && number <= max, "bounded-" + name);
            return number;
        }
    }
}

internal sealed class ProbeFailure(string assertion) : Exception(assertion)
{
    internal string Assertion { get; } = assertion;
}
