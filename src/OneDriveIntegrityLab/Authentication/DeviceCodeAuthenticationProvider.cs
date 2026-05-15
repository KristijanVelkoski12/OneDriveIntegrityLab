using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;

namespace OneDriveIntegrityLab.Authentication;

public sealed class DeviceCodeAuthenticationProvider : IAuthenticationProvider
{
    public static readonly string[] DefaultScopes =
        [
        "Files.ReadWrite",
        "User.Read",
        "offline_access"
        ];

    private readonly DeviceCodeAuthenticationOptions _options;
    private readonly ILogger<DeviceCodeAuthenticationProvider> _logger;

    public DeviceCodeAuthenticationProvider(
        DeviceCodeAuthenticationOptions options,
        ILogger<DeviceCodeAuthenticationProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ClientId);
        _options = options;
        _logger = logger;
    }

    public IReadOnlyList<string> Scopes => DefaultScopes;

    public TokenCredential CreateCredential()
    {
        var credentialOptions = new DeviceCodeCredentialOptions
        {
            ClientId = _options.ClientId,
            TenantId = _options.TenantId,
            DeviceCodeCallback = (info, _) =>
            {
                _logger.LogInformation(
                    "Sign in to complete authentication: open {VerificationUrl} and enter code {UserCode}.",
                    info.VerificationUri,
                    info.UserCode);
                return Task.CompletedTask;
            },
            DisableAutomaticAuthentication = false,
        };

        return new DeviceCodeCredential(credentialOptions);
    }
}

public sealed record DeviceCodeAuthenticationOptions
{
    public required string ClientId { get; init; }

    public string TenantId { get; init; } = "common";
}
