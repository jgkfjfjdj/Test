using Kindling.Net.Protocol;

namespace Kindling.Net.Server
{
    /// <summary>
    /// Checks the join ticket in a Hello. The production implementation verifies the backend's Ed25519
    /// join ticket (docs/protocol/join-tickets.md); it is not part of M1.
    /// </summary>
    public interface IJoinAuthenticator
    {
        bool TryAuthenticate(in HelloMessage hello, out string displayName, out string reason);
    }
}
