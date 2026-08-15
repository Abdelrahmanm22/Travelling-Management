namespace Travel.Domain.Services
{
    /// <summary>
    /// Who is making the current request. An abstraction rather than IHttpContextAccessor directly,
    /// because the audit interceptor lives in Travel.Repository and that project must stay free of
    /// ASP.NET Core — same rule that keeps IFileStorageService taking a Stream instead of IFormFile.
    /// Returns nulls outside a request (startup migrations, seeding, a future background job).
    /// </summary>
    public interface ICurrentUserService
    {
        string? UserId { get; }

        /// <summary>Display name for the audit trail and transaction ledger — a snapshot, not a lookup.</summary>
        string? UserName { get; }
    }
}
