using Microsoft.AspNetCore.Identity;

namespace Travel.Domain.Services
{
    public class OperationResult
    {
        public bool Succeeded { get; init; }

        public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

        public string Error => Errors.Count > 0 ? Errors[0] : string.Empty;

        public static OperationResult Ok() => new() { Succeeded = true };

        public static OperationResult Fail(params string[] errors) =>
            new() { Succeeded = false, Errors = errors };

        public static OperationResult From(IdentityResult result) =>
            result.Succeeded ? Ok() : Fail(result.Errors.Select(e => e.Description).ToArray());
    }

    public sealed class OperationResult<T> : OperationResult
    {
        public T? Value { get; init; }

        public static OperationResult<T> Ok(T value) => new() { Succeeded = true, Value = value };

        public static new OperationResult<T> Fail(params string[] errors) =>
            new() { Succeeded = false, Errors = errors };

        public static OperationResult<T> From(IdentityResult result) =>
            result.Succeeded
                ? new OperationResult<T> { Succeeded = true }
                : Fail(result.Errors.Select(e => e.Description).ToArray());
    }
}
