namespace SynthCohost.Protocol
{
    /// <summary>Serializer/parser boundary so wire handling can be replaced without changing session or features.</summary>
    public interface IProtocolCodec
    {
        string SerializeEnvelope<TPayload>(V2Envelope<TPayload> envelope);

        bool TrySerializeEnvelope<TPayload>(
            V2Envelope<TPayload> envelope,
            out string json,
            out ProtocolError error);

        bool TryParseEnvelope(
            string rawJson,
            out IncomingEnvelope envelope,
            out ProtocolError error);

        bool TryParsePayload<TPayload>(
            IncomingEnvelope envelope,
            out TPayload payload,
            out ProtocolError error);
    }
}
