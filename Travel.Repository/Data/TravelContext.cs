using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Travel.Domain.Models;
using Travel.Domain.Models.Identity;

namespace Travel.Repository.Data
{
    public class TravelContext : IdentityDbContext<AppUser>
    {
        public TravelContext(DbContextOptions<TravelContext> options) : base(options)
        {
        }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        }

        public DbSet<Destination> Destinations { get; set; }
        public DbSet<Package> Packages { get; set; }
        public DbSet<PackageSegment> PackageSegments { get; set; }
        public DbSet<Booking> Bookings { get; set; }
        public DbSet<BookingStay> BookingStays { get; set; }
        public DbSet<BookingGuest> BookingGuests { get; set; }
        public DbSet<BookingRoom> BookingRooms { get; set; }
        public DbSet<BlockServiceStatus> BlockServiceStatuses { get; set; }
    }
}
