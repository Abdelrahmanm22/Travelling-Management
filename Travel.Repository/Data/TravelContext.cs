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
        public DbSet<Nationality> Nationalities { get; set; }
        public DbSet<Package> Packages { get; set; }
        public DbSet<PackageSegment> PackageSegments { get; set; }
        public DbSet<Booking> Bookings { get; set; }
        public DbSet<BookingStay> BookingStays { get; set; }
        public DbSet<BookingGuest> BookingGuests { get; set; }
        public DbSet<BookingRoom> BookingRooms { get; set; }
        public DbSet<BlockServiceStatus> BlockServiceStatuses { get; set; }
        public DbSet<BlockAttachment> BlockAttachments { get; set; }
        public DbSet<BlockConfirmedBooking> BlockConfirmedBookings { get; set; }
        public DbSet<Tour> Tours { get; set; }
        public DbSet<BookingTour> BookingTours { get; set; }
        public DbSet<BookingTransaction> BookingTransactions { get; set; }
        public DbSet<TransactionAttachment> TransactionAttachments { get; set; }
        public DbSet<BookingAuditEvent> BookingAuditEvents { get; set; }
        public DbSet<BookingAuditChange> BookingAuditChanges { get; set; }
        public DbSet<MoneySafe> MoneySafes { get; set; }
        public DbSet<SafeEntry> SafeEntries { get; set; }
        public DbSet<SafeEntryAttachment> SafeEntryAttachments { get; set; }
        public DbSet<ExpenseType> ExpenseTypes { get; set; }
        public DbSet<BlockExpense> BlockExpenses { get; set; }
        public DbSet<BlockExpenseRate> BlockExpenseRates { get; set; }
        public DbSet<BlockExpenseGroup> BlockExpenseGroups { get; set; }
        public DbSet<BlockExpenseBooking> BlockExpenseBookings { get; set; }
    }
}
