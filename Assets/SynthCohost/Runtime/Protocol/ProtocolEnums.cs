using System;
using Newtonsoft.Json;

namespace SynthCohost.Protocol
{
    [JsonConverter(typeof(AvatarBehaviorJsonConverter))]
    public enum AvatarBehavior
    {
        Idle,
        Listening,
        Thinking,
        Speaking,
        Happy,
        Celebrate
    }

    [JsonConverter(typeof(AiEmotionJsonConverter))]
    public enum AiEmotion
    {
        Neutral,
        Happy,
        Excited,
        Concerned,
        Confused,
        Celebrate
    }

    [JsonConverter(typeof(AiIntentJsonConverter))]
    public enum AiIntent
    {
        Chat,
        Question,
        Command,
        Greeting,
        Farewell,
        Unknown
    }

    /// <summary>Explicit, culture-independent mappings between C# enums and protocol strings.</summary>
    public static class ProtocolEnumValues
    {
        public static bool TryParseAvatarBehavior(string value, out AvatarBehavior behavior)
        {
            switch (value)
            {
                case "idle": behavior = AvatarBehavior.Idle; return true;
                case "listening": behavior = AvatarBehavior.Listening; return true;
                case "thinking": behavior = AvatarBehavior.Thinking; return true;
                case "speaking": behavior = AvatarBehavior.Speaking; return true;
                case "happy": behavior = AvatarBehavior.Happy; return true;
                case "celebrate": behavior = AvatarBehavior.Celebrate; return true;
                default: behavior = default; return false;
            }
        }

        public static bool TryParseAiEmotion(string value, out AiEmotion emotion)
        {
            switch (value)
            {
                case "neutral": emotion = AiEmotion.Neutral; return true;
                case "happy": emotion = AiEmotion.Happy; return true;
                case "excited": emotion = AiEmotion.Excited; return true;
                case "concerned": emotion = AiEmotion.Concerned; return true;
                case "confused": emotion = AiEmotion.Confused; return true;
                case "celebrate": emotion = AiEmotion.Celebrate; return true;
                default: emotion = default; return false;
            }
        }

        public static bool TryParseAiIntent(string value, out AiIntent intent)
        {
            switch (value)
            {
                case "chat": intent = AiIntent.Chat; return true;
                case "question": intent = AiIntent.Question; return true;
                case "command": intent = AiIntent.Command; return true;
                case "greeting": intent = AiIntent.Greeting; return true;
                case "farewell": intent = AiIntent.Farewell; return true;
                case "unknown": intent = AiIntent.Unknown; return true;
                default: intent = default; return false;
            }
        }

        public static string ToWireValue(this AvatarBehavior behavior)
        {
            switch (behavior)
            {
                case AvatarBehavior.Idle: return "idle";
                case AvatarBehavior.Listening: return "listening";
                case AvatarBehavior.Thinking: return "thinking";
                case AvatarBehavior.Speaking: return "speaking";
                case AvatarBehavior.Happy: return "happy";
                case AvatarBehavior.Celebrate: return "celebrate";
                default: throw new ArgumentOutOfRangeException(nameof(behavior), behavior, "Unknown avatar behavior.");
            }
        }

        public static string ToWireValue(this AiEmotion emotion)
        {
            switch (emotion)
            {
                case AiEmotion.Neutral: return "neutral";
                case AiEmotion.Happy: return "happy";
                case AiEmotion.Excited: return "excited";
                case AiEmotion.Concerned: return "concerned";
                case AiEmotion.Confused: return "confused";
                case AiEmotion.Celebrate: return "celebrate";
                default: throw new ArgumentOutOfRangeException(nameof(emotion), emotion, "Unknown AI emotion.");
            }
        }

        public static string ToWireValue(this AiIntent intent)
        {
            switch (intent)
            {
                case AiIntent.Chat: return "chat";
                case AiIntent.Question: return "question";
                case AiIntent.Command: return "command";
                case AiIntent.Greeting: return "greeting";
                case AiIntent.Farewell: return "farewell";
                case AiIntent.Unknown: return "unknown";
                default: throw new ArgumentOutOfRangeException(nameof(intent), intent, "Unknown AI intent.");
            }
        }
    }

    internal sealed class AvatarBehaviorJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(AvatarBehavior);

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.String &&
                ProtocolEnumValues.TryParseAvatarBehavior((string)reader.Value, out var value))
            {
                return value;
            }

            throw new JsonSerializationException($"Unknown avatar behavior '{reader.Value}'.");
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            try
            {
                writer.WriteValue(((AvatarBehavior)value).ToWireValue());
            }
            catch (Exception exception) when (exception is InvalidCastException || exception is ArgumentOutOfRangeException)
            {
                throw new JsonSerializationException("Cannot serialize an unknown avatar behavior.", exception);
            }
        }
    }

    internal sealed class AiEmotionJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(AiEmotion);

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.String &&
                ProtocolEnumValues.TryParseAiEmotion((string)reader.Value, out var value))
            {
                return value;
            }

            throw new JsonSerializationException($"Unknown AI emotion '{reader.Value}'.");
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            try
            {
                writer.WriteValue(((AiEmotion)value).ToWireValue());
            }
            catch (Exception exception) when (exception is InvalidCastException || exception is ArgumentOutOfRangeException)
            {
                throw new JsonSerializationException("Cannot serialize an unknown AI emotion.", exception);
            }
        }
    }

    internal sealed class AiIntentJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(AiIntent);

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.String &&
                ProtocolEnumValues.TryParseAiIntent((string)reader.Value, out var value))
            {
                return value;
            }

            throw new JsonSerializationException($"Unknown AI intent '{reader.Value}'.");
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            try
            {
                writer.WriteValue(((AiIntent)value).ToWireValue());
            }
            catch (Exception exception) when (exception is InvalidCastException || exception is ArgumentOutOfRangeException)
            {
                throw new JsonSerializationException("Cannot serialize an unknown AI intent.", exception);
            }
        }
    }
}
