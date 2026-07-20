using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SynthCohost.Runtime.Development
{
    internal readonly struct LiveTestPlaceholderDraft
    {
        public LiveTestPlaceholderDraft(
            string endpointUrl,
            string accessToken,
            string avatarId,
            string refreshToken,
            string email,
            string password,
            string sourceSummary,
            string safeNotice)
        {
            EndpointUrl = endpointUrl ?? string.Empty;
            AccessToken = accessToken ?? string.Empty;
            AvatarId = avatarId ?? string.Empty;
            RefreshToken = refreshToken ?? string.Empty;
            Email = email ?? string.Empty;
            Password = password ?? string.Empty;
            SourceSummary = sourceSummary ?? "settings/manual";
            SafeNotice = safeNotice ?? string.Empty;
        }

        public string EndpointUrl { get; }
        public string AccessToken { get; }
        public string AvatarId { get; }
        public string RefreshToken { get; }
        public string Email { get; }
        public string Password { get; }
        public string SourceSummary { get; }
        public string SafeNotice { get; }

        public bool HasTokenRefreshCredentials =>
            HasValue(RefreshToken) || (HasValue(Email) && HasValue(Password));

        private static bool HasValue(string value)
        {
            return !string.IsNullOrWhiteSpace(value);
        }
    }

    internal static class LiveTestPlaceholderSource
    {
        internal const string EndpointEnvironmentVariable = "SYNTH_COHOST_ENDPOINT";
        internal const string TokenEnvironmentVariable = "SYNTH_COHOST_ACCESS_TOKEN";
        internal const string AvatarEnvironmentVariable = "SYNTH_COHOST_AVATAR_ID";
        internal const string LocalFileName = "SynthCohostLiveTest.local.json";
        private const long MaximumLocalFileBytes = 64 * 1024;

        [Serializable]
        private sealed class LocalPlaceholderJson
        {
            public string endpointUrl;
            public string accessToken;
            public string avatarId;
            public string refreshToken;
            public string email;
            public string password;
        }

        internal static LiveTestPlaceholderDraft Load(string fallbackEndpoint)
        {
            var localPath = GetDefaultLocalPath();
            string localJson = null;
            string safeNotice = string.Empty;
            try
            {
                if (File.Exists(localPath))
                {
                    var info = new FileInfo(localPath);
                    if (info.Length > MaximumLocalFileBytes)
                    {
                        safeNotice = "Local placeholder file was ignored because it is too large.";
                    }
                    else
                    {
                        localJson = File.ReadAllText(localPath);
                    }
                }
            }
            catch (Exception exception)
            {
                // Never include Exception.Message; a path/provider exception may contain secrets.
                safeNotice =
                    $"Local placeholder file could not be read ({exception.GetType().Name}).";
            }

            return Resolve(
                fallbackEndpoint,
                localJson,
                Environment.GetEnvironmentVariable(EndpointEnvironmentVariable),
                Environment.GetEnvironmentVariable(TokenEnvironmentVariable),
                Environment.GetEnvironmentVariable(AvatarEnvironmentVariable),
                safeNotice);
        }

        internal static LiveTestPlaceholderDraft Resolve(
            string fallbackEndpoint,
            string localJson,
            string environmentEndpoint,
            string environmentToken,
            string environmentAvatar,
            string priorSafeNotice = "")
        {
            LocalPlaceholderJson local = null;
            var safeNotice = priorSafeNotice ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(localJson))
            {
                var trimmedJson = localJson.Trim();
                if (!trimmedJson.StartsWith("{", StringComparison.Ordinal) ||
                    !trimmedJson.EndsWith("}", StringComparison.Ordinal))
                {
                    safeNotice = "Local placeholder file was ignored because its JSON is invalid.";
                }
                else
                {
                    try
                    {
                        local = JsonUtility.FromJson<LocalPlaceholderJson>(trimmedJson);
                        if (local == null)
                        {
                            safeNotice = "Local placeholder file was ignored because its JSON is invalid.";
                        }
                    }
                    catch (Exception exception)
                    {
                        safeNotice =
                            $"Local placeholder file was ignored ({exception.GetType().Name}).";
                    }
                }
            }

            var sources = new List<string>();
            if (local != null &&
                (HasValue(local.endpointUrl) ||
                 HasValue(local.accessToken) ||
                 HasValue(local.avatarId) ||
                 HasValue(local.refreshToken) ||
                 HasValue(local.email) ||
                 HasValue(local.password)))
            {
                sources.Add("local UserSettings file");
            }

            if (HasValue(environmentEndpoint) ||
                HasValue(environmentToken) ||
                HasValue(environmentAvatar))
            {
                sources.Add("process environment");
            }

            var endpoint = FirstValue(
                environmentEndpoint,
                local?.endpointUrl,
                fallbackEndpoint);
            var token = FirstValue(environmentToken, local?.accessToken);
            var avatar = FirstValue(environmentAvatar, local?.avatarId);
            return new LiveTestPlaceholderDraft(
                endpoint,
                token,
                avatar,
                local?.refreshToken,
                local?.email,
                local?.password,
                sources.Count == 0 ? "settings/manual" : string.Join(" + ", sources),
                safeNotice);
        }

        internal static bool TrySaveLocalDraft(
            string endpointUrl,
            string accessToken,
            string avatarId,
            string refreshToken,
            string email,
            string password,
            out string safeError)
        {
            safeError = string.Empty;
            try
            {
                var path = GetDefaultLocalPath();
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var draft = new LocalPlaceholderJson
                {
                    endpointUrl = endpointUrl ?? string.Empty,
                    accessToken = accessToken ?? string.Empty,
                    avatarId = avatarId ?? string.Empty,
                    refreshToken = refreshToken ?? string.Empty,
                    email = email ?? string.Empty,
                    password = password ?? string.Empty
                };
                File.WriteAllText(path, JsonUtility.ToJson(draft, true));
                return true;
            }
            catch (Exception exception)
            {
                safeError =
                    $"Local placeholder file could not be updated ({exception.GetType().Name}).";
                return false;
            }
        }

        internal static string GetDefaultLocalPath()
        {
            return Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "UserSettings",
                LocalFileName));
        }

        private static string FirstValue(params string[] values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            foreach (var value in values)
            {
                if (HasValue(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        private static bool HasValue(string value)
        {
            return !string.IsNullOrWhiteSpace(value);
        }
    }
}
