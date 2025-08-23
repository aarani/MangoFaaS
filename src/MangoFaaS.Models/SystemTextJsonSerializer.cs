using System.Text.Json;
using Confluent.Kafka;

namespace MangoFaaS.Models;

public class SystemTextJsonSerializer<T> : ISerializer<T>
{
    public byte[] Serialize(T data, SerializationContext context)
    {
        return JsonSerializer.SerializeToUtf8Bytes(data);
    }
}