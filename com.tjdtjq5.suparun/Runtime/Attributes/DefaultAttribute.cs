using System;

namespace Tjdtjq5.SupaRun
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class DefaultAttribute : Attribute
    {
        public object Value { get; }

        public DefaultAttribute(object value)
        {
            Value = value;
        }
    }
}
