using System;
using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Runtime.Authentication;

namespace SynthCohost.Runtime.Development
{
    internal enum LiveTestTokenRefreshMethod
    {
        None,
        RefreshToken,
        EmailPassword
    }

    internal readonly struct LiveTestTokenRefreshResult
    {
        internal LiveTestTokenRefreshResult(
            bool succeeded,
            string accessToken,
            string refreshToken,
            LiveTestTokenRefreshMethod method,
            string safeError)
        {
            Succeeded = succeeded;
            AccessToken = accessToken ?? string.Empty;
            RefreshToken = refreshToken ?? string.Empty;
            Method = method;
            SafeError = safeError ?? string.Empty;
        }

        internal bool Succeeded { get; }
        internal string AccessToken { get; }
        internal string RefreshToken { get; }
        internal LiveTestTokenRefreshMethod Method { get; }
        internal string SafeError { get; }

        internal static LiveTestTokenRefreshResult Fail(string safeError)
        {
            return new LiveTestTokenRefreshResult(
                false,
                string.Empty,
                string.Empty,
                LiveTestTokenRefreshMethod.None,
                safeError);
        }
    }

    /// <summary>
    /// Development helper over <see cref="AuthHttpClient"/> for the live-test panel.
    /// </summary>
    internal static class LiveTestAccessTokenRefresher
    {
        internal static bool TryBuildRestBaseUri(string websocketEndpoint, out Uri restBase, out string error)
        {
            return AuthHttpClient.TryBuildRestBaseUri(websocketEndpoint, out restBase, out error);
        }

        internal static async Task<LiveTestTokenRefreshResult> RefreshAsync(
            Uri restBase,
            string refreshToken,
            string email,
            string password,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                var refreshed = await AuthHttpClient.RefreshAsync(
                    restBase,
                    refreshToken,
                    cancellationToken);
                if (refreshed.Succeeded)
                {
                    return new LiveTestTokenRefreshResult(
                        true,
                        refreshed.Tokens.AccessToken,
                        refreshed.Tokens.RefreshToken,
                        LiveTestTokenRefreshMethod.RefreshToken,
                        string.Empty);
                }

                if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                {
                    return LiveTestTokenRefreshResult.Fail(refreshed.SafeError);
                }
            }

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                return LiveTestTokenRefreshResult.Fail(
                    "Token refresh needs refreshToken or email+password.");
            }

            var login = await AuthHttpClient.LoginAsync(
                restBase,
                email,
                password,
                cancellationToken);
            if (!login.Succeeded)
            {
                return LiveTestTokenRefreshResult.Fail(login.SafeError);
            }

            return new LiveTestTokenRefreshResult(
                true,
                login.Tokens.AccessToken,
                login.Tokens.RefreshToken,
                LiveTestTokenRefreshMethod.EmailPassword,
                string.Empty);
        }
    }
}
