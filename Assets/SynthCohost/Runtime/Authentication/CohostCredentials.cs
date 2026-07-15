using System;

namespace SynthCohost.Runtime.Authentication
{
    public readonly struct CohostCredentials
    {
        public CohostCredentials(string accessToken, Guid avatarId)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new ArgumentException("An access token is required.", nameof(accessToken));
            }

            if (avatarId == Guid.Empty)
            {
                throw new ArgumentException("A non-empty avatar ID is required.", nameof(avatarId));
            }

            AccessToken = accessToken;
            AvatarId = avatarId;
        }

        public string AccessToken { get; }
        public Guid AvatarId { get; }

        public bool IsValid => !string.IsNullOrWhiteSpace(AccessToken) && AvatarId != Guid.Empty;

        public override string ToString()
        {
            return $"CohostCredentials(avatar={AvatarId:D}, token=[REDACTED])";
        }
    }
}
