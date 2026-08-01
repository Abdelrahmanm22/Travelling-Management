using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Travel.Domain.Authorization;
using Travel.Domain.Enums;
using Travel.Domain.Models.Identity;

namespace Travel.Repository.Data
{
    public static class AppIdentityDbContextSeed
    {
        private const string SeedAdminUserName = "MustafaAdmin";

        /// <summary>
        /// Creates the SuperAdmin role if it's missing. It deliberately carries no permission claims —
        /// <c>PermissionAuthorizationHandler</c> short-circuits on the role itself.
        /// Guarded independently of the user seed so it also runs on an existing database.
        /// </summary>
        public static async Task SeedRolesAsync(RoleManager<IdentityRole> roleManager)
        {
            if (await roleManager.RoleExistsAsync(AppRoles.SuperAdmin))
                return;

            var result = await roleManager.CreateAsync(new IdentityRole(AppRoles.SuperAdmin));
            if (!result.Succeeded)
                throw new InvalidOperationException(
                    $"Failed to seed the {AppRoles.SuperAdmin} role: " +
                    string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        /// <summary>
        /// Makes sure at least one user holds SuperAdmin, so the app is never left unadministrable.
        /// </summary>
        public static async Task AssignSuperAdminAsync(UserManager<AppUser> userManager)
        {
            var existing = await userManager.GetUsersInRoleAsync(AppRoles.SuperAdmin);
            if (existing.Count > 0)
                return;

            var user = await userManager.FindByNameAsync(SeedAdminUserName)
                       ?? userManager.Users.OrderBy(u => u.Id).FirstOrDefault();

            if (user is null)
                return;

            await userManager.AddToRoleAsync(user, AppRoles.SuperAdmin);

            // The original seed never set these, which would leave a blank name in the users grid.
            if (string.IsNullOrWhiteSpace(user.FirstName) || string.IsNullOrWhiteSpace(user.LastName))
            {
                user.FirstName = "Mustafa";
                user.LastName = "Admin";
                if (!Enum.IsDefined(user.Gender))
                    user.Gender = Gender.Male;

                await userManager.UpdateAsync(user);
            }
        }

        public static async Task SeedUserAsync(UserManager<AppUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            if (!userManager.Users.Any())
            {
                var user = new AppUser
                {
                    UserName = "MustafaAdmin",
                    Email = "abdra1396@gmail.com",
                    PhoneNumber = "01015496488",
                };
                await userManager.CreateAsync(user, "Mm115599@");

                //var user1 = new AppUser
                //{
                //    UserName = "abdelrahman",
                //    Email = "abdelrahmanmohamed2293@gmail.com",
                //    PhoneNumber = "01015496488",
                //};
                //await userManager.CreateAsync(user1, "Ar115599@");

                //var user2 = new AppUser
                //{
                //    UserName = "Khaled",
                //    Email = "Khaledmramadan136@gmail.com",
                //    PhoneNumber = "01015496488",
                //};
                //await userManager.CreateAsync(user2, "Ar115599@");

                //var user3 = new AppUser
                //{
                //    UserName = "Omar",
                //    Email = "omar.ramadan2845@gmail.com",
                //    PhoneNumber = "01015496488"
                //};
                //await userManager.CreateAsync(user3, "Ar115599@");
            }
        }
    }
}
