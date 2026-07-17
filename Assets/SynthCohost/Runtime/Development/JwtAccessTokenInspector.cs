using System;
using System.Text;
using UnityEngine;

namespace SynthCohost.Runtime.Development
{
    internal static class JwtAccessTokenInspector
    {
        private const int MaximumTokenCharacters = 32 * 1024;

        [Serializable]
        private sealed class JwtPayload
        {
            public long exp;
        }

        internal static bool TryGetExpirationUtc(
            string accessToken,
            out DateTimeOffset expirationUtc)
        {
            expirationUtc = default;
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
                var payload = JsonUtility.FromJson<JwtPayload>(json);
                if (payload == null || payload.exp <= 0)
                {
                    return false;
                }

                expirationUtc = DateTimeOffset.FromUnixTimeSeconds(payload.exp);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
