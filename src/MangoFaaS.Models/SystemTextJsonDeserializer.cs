using System.Text.Json;
using Confluent.Kafka;

namespace MangoFaaS.Models;

public class SystemTextJsonDeserializer<T> : IDeserializer<T>
{
    public T Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context)
    {
        if (isNull) return default!;
        return JsonSerializer.Deserialize<T>(data) ?? throw new InvalidOperationException();
    }
}