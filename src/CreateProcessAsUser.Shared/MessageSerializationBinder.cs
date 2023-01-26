using System;
using System.Reflection;
using System.Runtime.Serialization;

namespace CreateProcessAsUser.Shared
{
    public sealed class MessageSerializationBinder : SerializationBinder
    {
        //https://stackoverflow.com/questions/5170333/binaryformatter-deserialize-unable-to-find-assembly-after-ilmerge
        public override Type? BindToType(string assemblyName, string typeName)
        {
            // For each assemblyName/typeName that you want to deserialize to
            // a different type, set typeToDeserialize to the desired type.
            string exeAssembly = Assembly.GetExecutingAssembly().FullName!;

            // The following line of code returns the type.
            return Type.GetType($"{typeName}, {exeAssembly}");
        }
    }
}
