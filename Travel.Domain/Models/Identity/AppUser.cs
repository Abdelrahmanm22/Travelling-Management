using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Travel.Domain.Enums;

namespace Travel.Domain.Models.Identity
{
    public class AppUser : IdentityUser
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;

        public string FullName => $"{FirstName} {LastName}".Trim();

        public Gender Gender { get; set; }
        public bool IsActive { get; set; } = true;

        /// <summary>Optional profile image URL.</summary>
        public string? ImageUrl { get; set; }
    }
}
