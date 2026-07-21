using System;
using RuntimeJwt = SynthCohost.Runtime.Authentication.JwtAccessTokenInspector;

namespace SynthCohost.Runtime.Development
{
    /// <summary>Development alias for the runtime JWT inspector.</summary>
    internal static class JwtAccessTokenInspector
    {
        internal static bool TryGetExpirationUtc(
            string accessToken,
            out DateTimeOffset expirationUtc)
        {
            return RuntimeJwt.TryGetExpirationUtc(accessToken, out expirationUtc);
        }
    }
}
