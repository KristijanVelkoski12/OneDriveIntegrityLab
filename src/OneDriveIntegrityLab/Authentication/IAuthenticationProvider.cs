using Azure.Core;

namespace OneDriveIntegrityLab.Authentication;

public interface IAuthenticationProvider
{
    IReadOnlyList<string> Scopes { get; }

    TokenCredential CreateCredential();
}
