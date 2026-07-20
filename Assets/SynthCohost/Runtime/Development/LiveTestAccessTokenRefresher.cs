using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Runtime.Configuration;
using UnityEngine;
using UnityEngine.Networking;

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
    /// Development-only helper that mints a short-lived access token from the live REST auth routes.
    /// Never logs credentials, response bodies, or exception messages.
    /// </summary>
    internal static class LiveTestAccessTokenRefresher
    {
        private const int MaximumResponseBytes = 16 * 1024;

        [Serializable]
        private sealed class AuthResponseJson
        {
            public string access_token;
            public string refresh_token;
        }

        [Serializable]
        private sealed class RefreshRequestJson
        {
            public string refresh_token;
        }

        [Serializable]
        private sealed class LoginRequestJson
        {
            public string email;
            public string password;
        }

        internal static bool TryBuildRestBaseUri(string websocketEndpoint, out Uri restBase, out string error)
        {
            restBase = null;
            error = string.Empty;
            if (!SynthCohostConnectionSettings.TryParseEndpoint(
                    websocketEndpoint,
                    out var websocketUri,
                    out error))
            {
                return false;
            }

            var builder = new UriBuilder(websocketUri)
            {
                Scheme = string.Equals(websocketUri.Scheme, "wss", StringComparison.OrdinalIgnoreCase)
                    ? "https"
                    : "http",
                Path = string.Empty,
                Query = string.Empty,
                Fragment = string.Empty
            };
            restBase = builder.Uri;
            return true;
        }

        internal static async Task<LiveTestTokenRefreshResult> RefreshAsync(
            Uri restBase,
            string refreshToken,
            string email,
            string password,
            CancellationToken cancellationToken)
        {
            if (restBase == null || !restBase.IsAbsoluteUri)
            {
                return LiveTestTokenRefreshResult.Fail("Token refresh failed: REST base URL is invalid.");
            }

            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                var refreshed = await PostAuthAsync(
                    new Uri(restBase, "/auth/refresh"),
                    JsonUtility.ToJson(new RefreshRequestJson { refresh_token = refreshToken.Trim() }),
                    LiveTestTokenRefreshMethod.RefreshToken,
                    cancellationToken);
                if (refreshed.Succeeded)
                {
                    return refreshed;
                }

                // Fall through to email/password when refresh is rejected or unavailable.
                if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                {
                    return refreshed;
                }
            }

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                return LiveTestTokenRefreshResult.Fail(
                    "Token refresh needs refreshToken or email+password in the local UserSettings draft.");
            }

            return await PostAuthAsync(
                new Uri(restBase, "/auth/login"),
                JsonUtility.ToJson(new LoginRequestJson
                {
                    email = email.Trim(),
                    password = password
                }),
                LiveTestTokenRefreshMethod.EmailPassword,
                cancellationToken);
        }

        private static async Task<LiveTestTokenRefreshResult> PostAuthAsync(
            Uri url,
            string jsonBody,
            LiveTestTokenRefreshMethod method,
            CancellationToken cancellationToken)
        {
            using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            var bodyBytes = Encoding.UTF8.GetBytes(jsonBody ?? "{}");
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 60;

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    request.Abort();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                await Task.Yield();
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (request.result != UnityWebRequest.Result.Success)
            {
                var status = request.responseCode;
                return LiveTestTokenRefreshResult.Fail(
                    status > 0
                        ? $"Token refresh failed (HTTP {status})."
                        : $"Token refresh failed ({request.result}).");
            }

            var raw = request.downloadHandler?.text ?? string.Empty;
            if (raw.Length > MaximumResponseBytes)
            {
                return LiveTestTokenRefreshResult.Fail("Token refresh failed: response was too large.");
            }

            AuthResponseJson parsed;
            try
            {
                parsed = JsonUtility.FromJson<AuthResponseJson>(raw);
            }
            catch (Exception exception)
            {
                return LiveTestTokenRefreshResult.Fail(
                    $"Token refresh failed while parsing the response ({exception.GetType().Name}).");
            }

            if (parsed == null || string.IsNullOrWhiteSpace(parsed.access_token))
            {
                return LiveTestTokenRefreshResult.Fail(
                    "Token refresh failed: access_token was missing from the response.");
            }

            return new LiveTestTokenRefreshResult(
                true,
                parsed.access_token.Trim(),
                string.IsNullOrWhiteSpace(parsed.refresh_token)
                    ? string.Empty
                    : parsed.refresh_token.Trim(),
                method,
                string.Empty);
        }
    }
}
