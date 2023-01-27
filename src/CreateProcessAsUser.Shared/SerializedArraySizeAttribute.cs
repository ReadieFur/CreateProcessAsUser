using System;

namespace CreateProcessAsUser.Shared
{
    internal class SerializedArraySizeAttribute : Attribute
    {
        public uint Size { get; }

        public SerializedArraySizeAttribute(uint size)
        {
            Size = size;
        }
    }
}
