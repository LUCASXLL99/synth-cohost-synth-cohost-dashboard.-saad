using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Runtime.Configuration;
using UnityEngine;
using UnityEngine.Networking;

namespace SynthCohost.Runtime.Authentication
{
    /// <summary>
    /// HTTP auth client for login / refresh / logout against the Synth Cohost REST host.
    /// Never logs tokens, passwords, or response bodies.
    /// </summary>
    public static class AuthHttpClient
    {
        private const int MaximumResponseBytes = 16 * 1024;
        public static readonly TimeSpan DefaultAccessTokenLifetime = TimeSpan.FromMinutes(15);
        public static readonly TimeSpan DefaultRefreshSkew = TimeSpan.FromMinutes(2);

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

        [Serializable]
        private sealed class LogoutRequestJson
        {
            public string refresh_token;
        }

        public static bool TryBuildRestBaseUri(string websocketEndpoint, out Uri restBase, out string error)
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

        public static Task<AuthHttpResult> LoginAsync(
            Uri restBase,
            string email,
            string password,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                return Task.FromResult(AuthHttpResult.Fail(
                    AuthHttpOperation.Login,
                    "Login needs email and password."));
            }

            return PostAuthAsync(
                restBase,
                "/auth/login",
                JsonUtility.ToJson(new LoginRequestJson
                {
                    email = email.Trim(),
                    password = password
                }),
                AuthHttpOperation.Login,
                cancellationToken);
        }

        public static Task<AuthHttpResult> RefreshAsync(
            Uri restBase,
            string refreshToken,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return Task.FromResult(AuthHttpResult.Fail(
                    AuthHttpOperation.Refresh,
                    "Refresh needs a refresh token.",
                    requiresLogin: true));
            }

            return PostAuthAsync(
                restBase,
                "/auth/refresh",
                JsonUtility.ToJson(new RefreshRequestJson { refresh_token = refreshToken.Trim() }),
                AuthHttpOperation.Refresh,
                cancellationToken);
        }

        public static async Task<AuthHttpResult> LogoutAsync(
            Uri restBase,
            string refreshToken,
            CancellationToken cancellationToken)
        {
            if (restBase == null || !restBase.IsAbsoluteUri)
            {
                return AuthHttpResult.Fail(AuthHttpOperation.Logout, "Logout failed: REST base URL is invalid.");
            }

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return AuthHttpResult.Success(AuthHttpOperation.Logout, default);
            }

            using var request = CreatePostRequest(
                new Uri(restBase, "/auth/logout"),
                JsonUtility.ToJson(new LogoutRequestJson { refresh_token = refreshToken.Trim() }));

            await SendAsync(request, cancellationToken);
            if (request.responseCode == 204 || request.result == UnityWebRequest.Result.Success)
            {
                return AuthHttpResult.Success(AuthHttpOperation.Logout, default);
            }

            return AuthHttpResult.Fail(
                AuthHttpOperation.Logout,
                request.responseCode > 0
                    ? $"Logout failed (HTTP {request.responseCode})."
                    : $"Logout failed ({request.result}).",
                request.responseCode);
        }

        private static async Task<AuthHttpResult> PostAuthAsync(
            Uri restBase,
            string path,
            string jsonBody,
            AuthHttpOperation operation,
            CancellationToken cancellationToken)
        {
            if (restBase == null || !restBase.IsAbsoluteUri)
            {
                return AuthHttpResult.Fail(operation, "Auth request failed: REST base URL is invalid.");
            }

            using var request = CreatePostRequest(new Uri(restBase, path), jsonBody);
            await SendAsync(request, cancellationToken);

            var status = request.responseCode;
            if (request.result != UnityWebRequest.Result.Success)
            {
                var requiresLogin = status == 401 && operation == AuthHttpOperation.Refresh;
                return AuthHttpResult.Fail(
                    operation,
                    status > 0
                        ? $"Auth request failed (HTTP {status})."
                        : $"Auth request failed ({request.result}).",
                    status,
                    requiresLogin);
            }

            var raw = request.downloadHandler?.text ?? string.Empty;
            if (raw.Length > MaximumResponseBytes)
            {
                return AuthHttpResult.Fail(operation, "Auth response was too large.");
            }

            AuthResponseJson parsed;
            try
            {
                parsed = JsonUtility.FromJson<AuthResponseJson>(raw);
            }
            catch (Exception exception)
            {
                return AuthHttpResult.Fail(
                    operation,
                    $"Auth response could not be parsed ({exception.GetType().Name}).");
            }

            if (parsed == null ||
                string.IsNullOrWhiteSpace(parsed.access_token) ||
                string.IsNullOrWhiteSpace(parsed.refresh_token))
            {
                return AuthHttpResult.Fail(
                    operation,
                    "Auth response was missing access_token or refresh_token.");
            }

            return AuthHttpResult.Success(
                operation,
                new AuthTokenPair(parsed.access_token, parsed.refresh_token));
        }

        private static UnityWebRequest CreatePostRequest(Uri url, string jsonBody)
        {
            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            var bodyBytes = Encoding.UTF8.GetBytes(jsonBody ?? "{}");
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 60;
            return request;
        }

        private static async Task SendAsync(UnityWebRequest request, CancellationToken cancellationToken)
        {
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
        }
    }
}
