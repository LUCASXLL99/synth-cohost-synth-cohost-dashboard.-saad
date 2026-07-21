using System;
using System.Text;
using UnityEngine;

namespace SynthCohost.Runtime.Authentication
{
    /// <summary>
    /// Reads JWT <c>exp</c> without validating the signature. Used only for client-side
    /// refresh scheduling and operator guidance — never as a security boundary.
    /// </summary>
    public static class JwtAccessTokenInspector
    {
        private const int MaximumTokenCharacters = 32 * 1024;

        [Serializable]
        private sealed class JwtPayload
        {
            public long exp;
            public long iat;
        }

        public static bool TryGetExpirationUtc(string accessToken, out DateTimeOffset expirationUtc)
        {
            expirationUtc = default;
            if (!TryReadPayload(accessToken, out var payload) || payload.exp <= 0)
            {
                return false;
            }

            expirationUtc = DateTimeOffset.FromUnixTimeSeconds(payload.exp);
            return true;
        }

        public static bool TryGetIssuedAtUtc(string accessToken, out DateTimeOffset issuedAtUtc)
        {
            issuedAtUtc = default;
            if (!TryReadPayload(accessToken, out var payload) || payload.iat <= 0)
            {
                return false;
            }

            issuedAtUtc = DateTimeOffset.FromUnixTimeSeconds(payload.iat);
            return true;
        }

        /// <summary>
        /// Returns when the client should refresh: <c>exp - skew</c>, or immediately when already
        /// inside the skew window / expired.
        /// </summary>
        public static bool TryGetRefreshDueUtc(
            string accessToken,
            TimeSpan refreshSkew,
            out DateTimeOffset refreshDueUtc)
        {
            refreshDueUtc = default;
            if (!TryGetExpirationUtc(accessToken, out var expirationUtc))
            {
                return false;
            }

            if (refreshSkew < TimeSpan.Zero)
            {
                refreshSkew = TimeSpan.Zero;
            }

            refreshDueUtc = expirationUtc - refreshSkew;
            return true;
        }

        private static bool TryReadPayload(string accessToken, out JwtPayload payload)
        {
            payload = null;
            if (string.IsNullOrWhiteSpace(accessToken) ||
                accessToken.Length > MaximumTokenCharacters)
            {
                return false;
            }

            var parts = accessToken.Split('.');
            if (parts.Length != 3 || string.IsNullOrEmpty(parts[1]))
            {
                return false;
            }

            try
            {
                var base64 = parts[1].Replace('-', '+').Replace('_', '/');
                switch (base64.Length % 4)
                {
                    case 2:
                        base64 += "==";
                        break;
                    case 3:
                        base64 += "=";
                        break;
                    case 1:
                        return false;
                }

                var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
                payload = JsonUtility.FromJson<JwtPayload>(json);
                return payload != null;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
