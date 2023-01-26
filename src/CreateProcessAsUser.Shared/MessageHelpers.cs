using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace CreateProcessAsUser.Shared
{
    public static class MessageHelpers
    {
        public static ReadOnlyMemory<byte> Serialize(SMessage message) => CSharpTools.Pipes.Helpers.Serialize(message);

        public static SMessage Deserialize(ReadOnlyMemory<byte> bytes)
        {
            MemoryStream stream = new MemoryStream(bytes.ToArray());
            BinaryFormatter formatter = new BinaryFormatter();
            formatter.Binder = new MessageSerializationBinder();
            return (SMessage)formatter.Deserialize(stream);
        }
    }
}
