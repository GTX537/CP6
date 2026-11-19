using System.Net;
using CP6.Client.Api;
using CP6.Client.Core;

namespace CP6.Mobile;

public sealed class DeviceActivationService
{
    private readonly NativeDeviceActivationService _activation;
    private readonly ClientOptions _options;
    private readonly MobileClientState _state;
    private readonly ClientDeviceHeartbeatLoop _heartbeat;

    public DeviceActivationService(
        NativeDeviceActivationService activation,
        ClientOptions options,
        MobileClientState state,
        ClientDeviceHeartbeatLoop heartbeat)
    {
        _activation = activation;
        _options = options;
        _state = state;
        _heartbeat = heartbeat;
    }

    public async Task<ActivatedClientDevice> ActivateAsync(
        string payload,
        CancellationToken ct = default)
    {
        var ticket = Parse(payload);
        var previous = _options.ApiBaseAddress;
        _options.ApiBaseAddress = ticket.Server;
        try
        {
            var result = await _activation.ActivateAsync(ticket.Server, ticket.Tenant, ticket.Token, ticket.Operation, ct);
            Preferences.Default.Set("cp6.api-url", ticket.Server.AbsoluteUri);
            Preferences.Default.Set("cp6.tenant-code", result.TenantCode);
            MobileScannerSettings.Save(ticket.Scanner);
            _state.SetDeviceActivated(true);
            _heartbeat.RequestImmediate();
            return result;
        }
        catch
        {
            _options.ApiBaseAddress = previous;
            throw;
        }
    }

    private static ActivationTicket Parse(string payload)
    {
        if (!Uri.TryCreate(payload.Trim(), UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, "cp6-activate", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("WM-DEVICE-ACTIVATION-QR-INVALID");
        var values = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Split('=', 2))
            .Where(x => x.Length == 2)
            .ToDictionary(
                x => WebUtility.UrlDecode(x[0]),
                x => WebUtility.UrlDecode(x[1]),
                StringComparer.OrdinalIgnoreCase);
        if (!values.TryGetValue("server", out var server)
            || !Uri.TryCreate(server, UriKind.Absolute, out var serverUri)
            || serverUri.Scheme is not ("https" or "http")
            || !values.TryGetValue("tenant", out var tenant)
            || string.IsNullOrWhiteSpace(tenant)
            || !values.TryGetValue("token", out var token)
            || string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("WM-DEVICE-ACTIVATION-QR-INVALID");

        var prefix = values.GetValueOrDefault("scanPrefix") ?? string.Empty;
        var suffix = values.GetValueOrDefault("scanSuffix") ?? string.Empty;
        if (prefix.Length > MobileScannerSettings.MaximumFramingLength
            || suffix.Length > MobileScannerSettings.MaximumFramingLength)
            throw new InvalidOperationException("WM-DEVICE-ACTIVATION-QR-INVALID");

        var terminatorValue = values.GetValueOrDefault("scanTerminator") ?? "Enter";
        if (!Enum.TryParse<ScannerHidTerminator>(
                terminatorValue,
                ignoreCase: true,
                out var terminator))
            throw new InvalidOperationException("WM-DEVICE-ACTIVATION-QR-INVALID");

        var duplicateMilliseconds = ScannerInputOptions.DefaultDuplicateWindowMilliseconds;
        var duplicateValue = values.GetValueOrDefault("scanDuplicateMs");
        if (duplicateValue != null
            && (!int.TryParse(duplicateValue, out duplicateMilliseconds)
                || duplicateMilliseconds
                    < MobileScannerSettings.MinimumDuplicateWindowMilliseconds
                || duplicateMilliseconds
                    > MobileScannerSettings.MaximumDuplicateWindowMilliseconds))
            throw new InvalidOperationException("WM-DEVICE-ACTIVATION-QR-INVALID");

        return new ActivationTicket(
            new Uri(serverUri.AbsoluteUri.EndsWith('/') ? serverUri.AbsoluteUri : $"{serverUri}/"),
            tenant.Trim(),
            token.Trim(),
            ParseOperation(values),
            new ScannerInputOptions
            {
                Prefix = prefix,
                Suffix = suffix,
                HidTerminator = terminator,
                DuplicateWindow = TimeSpan.FromMilliseconds(duplicateMilliseconds),
            });
    }

    private sealed record ActivationTicket(
        Uri Server,
        string Tenant,
        string Token,
        Guid? Operation,
        ScannerInputOptions Scanner);

    private static Guid? ParseOperation(IReadOnlyDictionary<string, string> values)
    {
        if (!values.TryGetValue("operation", out var provided)) return null;
        if (Guid.TryParse(provided, out var id) && id != Guid.Empty) return id;
        throw new InvalidOperationException("WM-DEVICE-ACTIVATION-QR-INVALID");
    }
}
