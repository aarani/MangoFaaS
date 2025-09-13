using System.Text.Json;
using Confluent.Kafka;
using Google.Protobuf;

namespace MangoFaaS.Models.Helpers;

public class ProtobufSerializer<T> : ISerializer<T> where T: IMessage<T>
{
    public byte[] Serialize(T data, SerializationContext context)
    {
        var result = new byte[data.CalculateSize()];
        data.WriteTo(result);
        return result;
    }
}