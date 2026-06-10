namespace LongBetterWindows.Host.Contracts
{
    public class HostApiResponse
    {
        public bool IsSuccess { get; init; }
        public ApiErrorCode ErrorCode { get; init; }
        public string? ErrorMessage { get; init; }

        public static HostApiResponse Success() => new() { IsSuccess = true };
        public static HostApiResponse Failure(ApiErrorCode code, string? message = null)
            => new() { IsSuccess = false, ErrorCode = code, ErrorMessage = message };
    }

    public class HostApiResponse<T> : HostApiResponse
    {
        public T? Data { get; init; }

        public static HostApiResponse<T> Success(T data)
            => new() { IsSuccess = true, Data = data };
        public new static HostApiResponse<T> Failure(ApiErrorCode code, string? message = null)
            => new() { IsSuccess = false, ErrorCode = code, ErrorMessage = message };
    }
}
