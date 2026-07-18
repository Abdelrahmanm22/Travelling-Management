using Travel.Domain.Enums;

namespace Travel.Presentation.Helpers
{
    public static class AvatarHelper
    {
        public static string Resolve(string? imageUrl, Gender gender) =>
            !string.IsNullOrEmpty(imageUrl)
                ? imageUrl
                : (gender == Gender.Female
                    ? "/back/assets/images/users/useraya.png"
                    : "/back/assets/images/users/user.png");
    }
}