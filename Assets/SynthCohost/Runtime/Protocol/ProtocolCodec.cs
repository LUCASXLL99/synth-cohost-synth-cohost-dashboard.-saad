using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SynthCohost.Protocol
{
    /// <summary>Newtonsoft-based serializer and defensive two-stage parser for deployed v2 frames.</summary>
    public sealed class ProtocolCodec : IProtocolCodec
    {
        private readonly JsonSerializerSettings _settings;

        public ProtocolCodec()
        {
            _settings = new JsonSerializerSettings
            {
                Culture = CultureInfo.InvariantCulture,
                DateParseHandling = DateParseHandling.None,
                Formatting = Formatting.None,
                // Required known fields are enforced by DTO annotations. Extra fields remain additive-safe.
                MissingMemberHandling = MissingMemberHandling.Ignore,
                NullValueHandling = NullValueHandling.Include
            };
        }

        public string SerializeEnvelope<TPayload>(V2Envelope<TPayload> envelope)
        {
            if (!TrySerializeEnvelope(envelope, out var json, out var error))
            {
                throw new ProtocolException(error);
            }

            return json;
        }

        public bool TrySerializeEnvelope<TPayload>(
            V2Envelope<TPayload> envelope,
            out string json,
            out ProtocolError error)
        {
            json = null;
            error = ProtocolError.None;

            if (envelope == null)
            {
                error = new ProtocolError(ProtocolErrorCode.SerializationFailed, "Envelope is required.");
                return false;
            }

            if (!ValidateEnvelopeForSerialization(envelope, out error) ||
                !ValidateTypedPayload(envelope.EventType, envelope.Payload, out error))
            {
                return false;
            }

            try
            {
                json = JsonConvert.SerializeObject(envelope, _settings);
            }
            catch (Exception exception) when (exception is JsonException || exception is NotSupportedException)
            {
                error = new ProtocolError(ProtocolErrorCode.SerializationFailed, "Envelope could not be serialized.");
                return false;
            }

            var size = ProtocolValidation.ValidateJsonSize(json);
            if (!size.IsValid)
            {
                json = null;
                error = size.Error;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Validates only the common envelope. Unknown event names return true and remain untyped by design.
        /// </summary>
        public bool TryParseEnvelope(string rawJson, out IncomingEnvelope envelope, out ProtocolError error)
        {
            envelope = null;
            error = ProtocolError.None;

            var size = ProtocolValidation.ValidateJsonSize(rawJson);
            if (!size.IsValid)
            {
                error = size.Error;
                return false;
            }

            JObject root;
            try
            {
                using (var textReader = new StringReader(rawJson))
                using (var jsonReader = new JsonTextReader(textReader)
                {
                    DateParseHandling = DateParseHandling.None,
                    MaxDepth = ProtocolConstants.MaxJsonDepth
                })
                {
                    var token = JToken.ReadFrom(
                        jsonReader,
                        new JsonLoadSettings
                        {
                            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                            LineInfoHandling = LineInfoHandling.Ignore
                        });

                    if (!(token is JObject parsedRoot))
                    {
                        error = new ProtocolError(ProtocolErrorCode.RootNotObject, "Envelope root must be a JSON object.");
                        return false;
                    }

                    if (jsonReader.Read())
                    {
                        error = new ProtocolError(ProtocolErrorCode.InvalidJson, "JSON frame has trailing content.");
                        return false;
                    }

                    root = parsedRoot;
                }
            }
            catch (JsonReaderException exception)
            {
                var code = exception.Message.IndexOf("MaxDepth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           exception.Message.IndexOf("depth", StringComparison.OrdinalIgnoreCase) >= 0
                    ? ProtocolErrorCode.JsonTooDeep
                    : ProtocolErrorCode.InvalidJson;
                error = new ProtocolError(code, "JSON frame is malformed or exceeds the nesting limit.");
                return false;
            }
            catch (JsonException)
            {
                error = new ProtocolError(ProtocolErrorCode.InvalidJson, "JSON frame is malformed.");
                return false;
            }

            if (!TryReadInteger(root, "v", out var version, out error) ||
                !TryReadString(root, "type", out var eventType, out error) ||
                !TryReadString(root, "session_id", out var sessionId, out error) ||
                !TryReadString(root, "ts", out var timestamp, out error) ||
                !TryReadObject(root, "payload", out var payload, out error))
            {
                return false;
            }

            if (version != ProtocolConstants.DeployedVersion)
            {
                error = new ProtocolError(
                    ProtocolErrorCode.UnsupportedVersion,
                    $"Unsupported protocol version {version}; expected {ProtocolConstants.DeployedVersion}.");
                return false;
            }

            if (string.IsNullOrEmpty(eventType))
            {
                error = new ProtocolError(ProtocolErrorCode.InvalidEventType, "Envelope type must not be empty.");
                return false;
            }

            var sessionValidation = ProtocolValidation.ValidateSessionId(sessionId);
            if (!sessionValidation.IsValid)
            {
                error = sessionValidation.Error;
                return false;
            }

            if (!ProtocolValidation.TryParseUtcTimestamp(timestamp, out var timestampUtc))
            {
                error = new ProtocolError(
                    ProtocolErrorCode.InvalidTimestamp,
                    "Envelope ts must be an RFC3339 timestamp with a UTC offset.");
                return false;
            }

            envelope = new IncomingEnvelope(version, eventType, sessionId, timestamp, timestampUtc, payload);
            return true;
        }

        public bool TryParsePayload<TPayload>(
            IncomingEnvelope envelope,
            out TPayload payload,
            out ProtocolError error)
        {
            payload = default;
            error = ProtocolError.None;

            if (envelope == null)
            {
                error = new ProtocolError(ProtocolErrorCode.InvalidPayload, "Envelope is required.");
                return false;
            }

            if (!PayloadTypeMatchesEvent<TPayload>(envelope.EventType))
            {
                error = new ProtocolError(
                    ProtocolErrorCode.EventTypeMismatch,
                    $"Event '{envelope.EventType}' does not match payload type {typeof(TPayload).Name}.");
                return false;
            }

            try
            {
                payload = envelope.Payload.ToObject<TPayload>(JsonSerializer.Create(_settings));
            }
            catch (JsonException)
            {
                error = new ProtocolError(
                    ProtocolErrorCode.InvalidPayload,
                    $"Payload for '{envelope.EventType}' is malformed.");
                return false;
            }

            if (!ValidateTypedPayload(envelope.EventType, payload, out error))
            {
                payload = default;
                return false;
            }

            return true;
        }

        private static bool ValidateEnvelopeForSerialization<TPayload>(
            V2Envelope<TPayload> envelope,
            out ProtocolError error)
        {
            error = ProtocolError.None;
            if (envelope.Version != ProtocolConstants.DeployedVersion)
            {
                error = new ProtocolError(ProtocolErrorCode.UnsupportedVersion, "Only deployed protocol v2 may be emitted.");
                return false;
            }

            if (string.IsNullOrEmpty(envelope.EventType))
            {
                error = new ProtocolError(ProtocolErrorCode.InvalidEventType, "Envelope type is required.");
                return false;
            }

            var sessionValidation = ProtocolValidation.ValidateSessionId(envelope.SessionId);
            if (!sessionValidation.IsValid)
            {
                error = sessionValidation.Error;
                return false;
            }

            if (!ProtocolValidation.TryParseUtcTimestamp(envelope.Timestamp, out _))
            {
                error = new ProtocolError(ProtocolErrorCode.InvalidTimestamp, "Envelope ts must be an RFC3339 UTC timestamp.");
                return false;
            }

            if (ReferenceEquals(envelope.Payload, null))
            {
                error = new ProtocolError(ProtocolErrorCode.InvalidPayload, "Envelope payload is required.");
                return false;
            }

            return true;
        }

        private static bool ValidateTypedPayload(string eventType, object payload, out ProtocolError error)
        {
            error = ProtocolError.None;

            switch (eventType)
            {
                case ProtocolEventTypes.Auth:
                    if (!(payload is AuthPayload auth)) return WrongPayload(eventType, out error);
                    var token = ProtocolValidation.ValidateAccessToken(auth.Token);
                    if (!token.IsValid) { error = token.Error; return false; }
                    var avatar = ProtocolValidation.ValidateAvatarId(auth.AvatarId);
                    if (!avatar.IsValid) { error = avatar.Error; return false; }
                    return true;

                case ProtocolEventTypes.Heartbeat:
                    return payload is HeartbeatPayload || WrongPayload(eventType, out error);

                case ProtocolEventTypes.SttPartial:
                    if (!(payload is SttPartialPayload partial)) return WrongPayload(eventType, out error);
                    if (partial.Final)
                    {
                        error = new ProtocolError(ProtocolErrorCode.InvalidPayload, "stt.partial requires final=false.");
                        return false;
                    }
                    return UseValidation(ProtocolValidation.ValidateSttText(partial.Text), out error);

                case ProtocolEventTypes.SttFinal:
                    if (!(payload is SttFinalPayload final)) return WrongPayload(eventType, out error);
                    if (!final.Final)
                    {
                        error = new ProtocolError(ProtocolErrorCode.InvalidPayload, "stt.final requires final=true.");
                        return false;
                    }
                    return UseValidation(ProtocolValidation.ValidateSttText(final.Text), out error);

                case ProtocolEventTypes.StateAck:
                    return payload is StateAckPayload || WrongPayload(eventType, out error);

                case ProtocolEventTypes.AvatarState:
                    return payload is AvatarStatePayload || WrongPayload(eventType, out error);

                case ProtocolEventTypes.AiResponse:
                    if (!(payload is AiResponsePayload response)) return WrongPayload(eventType, out error);
                    if (string.IsNullOrEmpty(response.Text))
                    {
                        error = new ProtocolError(ProtocolErrorCode.InvalidPayload, "ai.response text is required.");
                        return false;
                    }
                    return true;

                case ProtocolEventTypes.SystemError:
                    if (!(payload is SystemErrorPayload systemError)) return WrongPayload(eventType, out error);
                    if (string.IsNullOrEmpty(systemError.Code) || systemError.Message == null)
                    {
                        error = new ProtocolError(ProtocolErrorCode.InvalidPayload, "system.error requires code and message.");
                        return false;
                    }
                    return true;

                case ProtocolEventTypes.SpeechAudio:
                    if (!(payload is SpeechAudioPayload speech)) return WrongPayload(eventType, out error);
                    if (speech.Audio == null || string.IsNullOrEmpty(speech.Audio.Data))
                    {
                        error = new ProtocolError(ProtocolErrorCode.InvalidPayload, "speech.audio requires inline audio data.");
                        return false;
                    }

                    if (!speech.TryGetAudioBytes(out _))
                    {
                        error = new ProtocolError(ProtocolErrorCode.InvalidPayload, "speech.audio data is not valid base64.");
                        return false;
                    }

                    return true;

                case ProtocolEventTypes.SpeechFailed:
                    return payload is SpeechFailedPayload || WrongPayload(eventType, out error);

                default:
                    // Unknown events are envelope-inspectable, but this codec has no typed contract for them.
                    error = new ProtocolError(ProtocolErrorCode.InvalidEventType, $"No typed payload contract exists for '{eventType}'.");
                    return false;
            }
        }

        private static bool PayloadTypeMatchesEvent<TPayload>(string eventType)
        {
            var type = typeof(TPayload);
            switch (eventType)
            {
                case ProtocolEventTypes.Auth: return type == typeof(AuthPayload);
                case ProtocolEventTypes.Heartbeat: return type == typeof(HeartbeatPayload);
                case ProtocolEventTypes.SttPartial: return type == typeof(SttPartialPayload);
                case ProtocolEventTypes.SttFinal: return type == typeof(SttFinalPayload);
                case ProtocolEventTypes.StateAck: return type == typeof(StateAckPayload);
                case ProtocolEventTypes.AvatarState: return type == typeof(AvatarStatePayload);
                case ProtocolEventTypes.AiResponse: return type == typeof(AiResponsePayload);
                case ProtocolEventTypes.SystemError: return type == typeof(SystemErrorPayload);
                case ProtocolEventTypes.SpeechAudio: return type == typeof(SpeechAudioPayload);
                case ProtocolEventTypes.SpeechFailed: return type == typeof(SpeechFailedPayload);
                default: return false;
            }
        }

        private static bool TryReadInteger(JObject root, string name, out int value, out ProtocolError error)
        {
            value = default;
            error = ProtocolError.None;
            var token = root.GetValue(name, StringComparison.Ordinal);
            if (token == null)
            {
                error = new ProtocolError(ProtocolErrorCode.MissingField, $"Envelope field '{name}' is required.");
                return false;
            }
            if (token.Type != JTokenType.Integer)
            {
                error = new ProtocolError(ProtocolErrorCode.InvalidFieldType, $"Envelope field '{name}' must be an integer.");
                return false;
            }
            try
            {
                value = token.Value<int>();
                return true;
            }
            catch (Exception exception) when (exception is OverflowException || exception is FormatException)
            {
                error = new ProtocolError(ProtocolErrorCode.InvalidFieldType, $"Envelope field '{name}' is outside the Int32 range.");
                return false;
            }
        }

        private static bool TryReadString(JObject root, string name, out string value, out ProtocolError error)
        {
            value = null;
            error = ProtocolError.None;
            var token = root.GetValue(name, StringComparison.Ordinal);
            if (token == null)
            {
                error = new ProtocolError(ProtocolErrorCode.MissingField, $"Envelope field '{name}' is required.");
                return false;
            }
            if (token.Type != JTokenType.String)
            {
                error = new ProtocolError(ProtocolErrorCode.InvalidFieldType, $"Envelope field '{name}' must be a string.");
                return false;
            }
            value = token.Value<string>();
            return true;
        }

        private static bool TryReadObject(JObject root, string name, out JObject value, out ProtocolError error)
        {
            value = null;
            error = ProtocolError.None;
            var token = root.GetValue(name, StringComparison.Ordinal);
            if (token == null)
            {
                error = new ProtocolError(ProtocolErrorCode.MissingField, $"Envelope field '{name}' is required.");
                return false;
            }
            if (!(token is JObject payload))
            {
                error = new ProtocolError(ProtocolErrorCode.InvalidFieldType, $"Envelope field '{name}' must be an object.");
                return false;
            }
            value = payload;
            return true;
        }

        private static bool WrongPayload(string eventType, out ProtocolError error)
        {
            error = new ProtocolError(ProtocolErrorCode.EventTypeMismatch, $"Payload type does not match '{eventType}'.");
            return false;
        }

        private static bool UseValidation(ProtocolValidationResult validation, out ProtocolError error)
        {
            error = validation.Error;
            return validation.IsValid;
        }
    }
}
