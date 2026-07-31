using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Travel.Domain.Models.Identity;

namespace Travel.Repository.Data
{
    public static class AppIdentityDbContextSeed
    {
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
