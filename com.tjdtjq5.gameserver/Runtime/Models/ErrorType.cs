namespace Tjdtjq5.GameServer
{
    public enum ErrorType
    {
        None,
        NetworkError,
        Timeout,
        ServerError,
        AuthExpired,
        AuthFailed,
        BadRequest,
        NotFound,
        RateLimit
    }
}
