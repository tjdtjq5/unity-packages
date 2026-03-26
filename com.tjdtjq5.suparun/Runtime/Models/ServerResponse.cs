using System;

namespace Tjdtjq5.SupaRun
{
    [Serializable]
    public class ServerResponse<T>
    {
        public bool success;
        public T data;
        public string error;
        public ErrorType errorType;
        public int statusCode;
    }

    [Serializable]
    public class ServerResponse
    {
        public bool success;
        public string error;
        public ErrorType errorType;
        public int statusCode;
    }
}
