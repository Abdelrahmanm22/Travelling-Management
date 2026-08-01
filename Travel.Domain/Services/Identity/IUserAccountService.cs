using Travel.Domain.Enums;

namespace Travel.Domain.Services.Identity
{
    public sealed record UserSummaryDto(
        string Id,
        string FullName,
        string UserName,
        string Email,
        string? PhoneNumber,
        string? ImageUrl,
        Gender Gender,
        bool IsActive,
        IReadOnlyList<string> Roles);

    public sealed record UserDetailsDto(
        string Id,
        string FirstName,
        string LastName,
        string FullName,
        string UserName,
        string Email,
        string? PhoneNumber,
        Gender Gender,
        bool IsActive,
        string? ImageUrl,
        IReadOnlyList<string> Roles,
        IReadOnlyList<string> DirectPermissions,
        // Permission value → the role names granting it.
        IReadOnlyDictionary<string, IReadOnlyList<string>> InheritedPermissions);

    public sealed record CreateUserRequest(
        string FirstName,
        string LastName,
        string UserName,
        string Email,
        string? PhoneNumber,
        Gender Gender,
        bool IsActive,
        string Password,
        string? ImageUrl,
        IEnumerable<string> Roles);

    public sealed record UpdateUserRequest(
        string Id,
        string FirstName,
        string LastName,
        string UserName,
        string Email,
        string? PhoneNumber,
        Gender Gender,
        bool IsActive,
        string? ImageUrl,
        IEnumerable<string> Roles);

    public interface IUserAccountService
    {
        Task<IReadOnlyList<UserSummaryDto>> GetAllAsync();

        Task<UserDetailsDto?> GetAsync(string userId);

        Task<OperationResult<string>> CreateAsync(CreateUserRequest request);

        Task<OperationResult> UpdateAsync(UpdateUserRequest request, string currentUserId);

        Task<OperationResult> DeleteAsync(string userId, string currentUserId);

        Task<OperationResult> SetActiveAsync(string userId, bool isActive, string currentUserId);

        Task<OperationResult> ResetPasswordAsync(string userId, string newPassword);

        Task<OperationResult> ReplaceDirectPermissionsAsync(string userId, IEnumerable<string> permissions);

        Task<bool> IsUserNameAvailableAsync(string userName, string? excludeUserId);

        Task<bool> IsEmailAvailableAsync(string email, string? excludeUserId);
    }
}
