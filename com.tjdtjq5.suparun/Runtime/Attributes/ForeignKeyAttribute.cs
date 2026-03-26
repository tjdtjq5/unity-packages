using System;

namespace Tjdtjq5.SupaRun
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class ForeignKeyAttribute : Attribute
    {
        public Type ReferenceType { get; }

        public ForeignKeyAttribute(Type referenceType)
        {
            ReferenceType = referenceType;
        }
    }
}
