using System;

namespace Tjdtjq5.GameServer
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class MaxLengthAttribute : Attribute
    {
        public int Length { get; }

        public MaxLengthAttribute(int length)
        {
            Length = length;
        }
    }
}
