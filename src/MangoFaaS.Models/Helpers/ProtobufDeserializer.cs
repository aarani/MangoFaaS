using System.Text.Json;
using Confluent.Kafka;
using Google.Protobuf;

namespace MangoFaaS.Models.Helpers;

public class ProtobufDeserializer<T> : IDeserializer<T> where T: IMessage<T>, new()
{
    public T Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context)
    {
        if (isNull) return default!;
        return new MessageParser<T>(() => new T()).ParseFrom(data);
    }
}