using Travel.Domain.Enums;

namespace Travel.Domain.Services.Identity
{
    public sealed record RoleSummaryDto(
        string Id,
        string Name,
        int UserCount,
        int PermissionCount,
        bool IsProtected);

    public sealed record RoleMemberDto(
        string Id,
        string FullName,
        string UserName,
        string? ImageUrl,
        Gender Gender,
        bool IsActive);

    public sealed record RoleDetailsDto(
        string Id,
        string Name,
        bool IsProtected,
        IReadOnlyList<string> Permissions,
        IReadOnlyList<RoleMemberDto> Members);

    public interface IRoleService
    {
        Task<IReadOnlyList<RoleSummaryDto>> GetAllAsync();

        Task<RoleDetailsDto?> GetAsync(string roleId);

        Task<IReadOnlyList<string>> GetPermissionsAsync(string roleId);

        Task<OperationResult<string>> CreateAsync(string name, IEnumerable<string> permissions);

        Task<OperationResult> UpdateAsync(string roleId, string name, IEnumerable<string> permissions);

        Task<OperationResult> DeleteAsync(string roleId);

        Task<bool> IsNameAvailableAsync(string name, string? excludeRoleId);
    }
}
