using System;

namespace SynthCohost.Runtime.Authentication
{
    public readonly struct AuthTokenPair
    {
        public AuthTokenPair(string accessToken, string refreshToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new ArgumentException("An access token is required.", nameof(accessToken));
            }

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                throw new ArgumentException("A refresh token is required.", nameof(refreshToken));
            }

            AccessToken = accessToken.Trim();
            RefreshToken = refreshToken.Trim();
        }

        public string AccessToken { get; }
        public string RefreshToken { get; }

        public override string ToString()
        {
            return "AuthTokenPair(access=[REDACTED], refresh=[REDACTED])";
        }
    }

    public enum AuthHttpOperation
    {
        None,
        Login,
        Refresh,
        Logout
    }

    public readonly struct AuthHttpResult
    {
        public AuthHttpResult(
            bool succeeded,
            AuthHttpOperation operation,
            AuthTokenPair tokens,
            long httpStatus,
            string safeError,
            bool requiresLogin)
        {
            Succeeded = succeeded;
            Operation = operation;
            Tokens = tokens;
            HttpStatus = httpStatus;
            SafeError = safeError ?? string.Empty;
            RequiresLogin = requiresLogin;
        }

        public bool Succeeded { get; }
        public AuthHttpOperation Operation { get; }
        public AuthTokenPair Tokens { get; }
        public long HttpStatus { get; }
        public string SafeError { get; }
        public bool RequiresLogin { get; }

        public static AuthHttpResult Fail(
            AuthHttpOperation operation,
            string safeError,
            long httpStatus = 0,
            bool requiresLogin = false)
        {
            return new AuthHttpResult(
                false,
                operation,
                default,
                httpStatus,
                safeError,
                requiresLogin);
        }

        public static AuthHttpResult Success(AuthHttpOperation operation, AuthTokenPair tokens)
        {
            return new AuthHttpResult(true, operation, tokens, 200, string.Empty, false);
        }
    }
}
