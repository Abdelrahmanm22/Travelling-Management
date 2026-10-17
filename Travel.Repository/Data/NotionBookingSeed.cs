using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Travel.Domain.Enums;
using Travel.Domain.Models;
using Travel.Domain.Services.BlockServices;
using Travel.Domain.Services.CodeGeneration;
using Travel.Domain.Services.Itinerary;

namespace Travel.Repository.Data
{
    /// <summary>
    /// One-off import of the bookings exported from Notion's "Reservas de Viagem" into
    /// <c>Data/SeedData/notion-bookings.json</c>. Safe to run more than once: a booking is skipped when
    /// its Notion URL is already in some booking's Notes, or when a booking with the same lead guest
    /// name already exists (many of these clients were entered by hand before the import).
    /// </summary>
    public static class NotionBookingSeed
    {
        public static readonly string DefaultPath =
            Path.Combine(AppContext.BaseDirectory, "Data", "SeedData", "notion-bookings.json");

        private const string ImportedBy = "Notion import";

        public static async Task SeedAsync(
            TravelContext context,
            ICodeGeneratorService codes,
            IItineraryGeneratorService generator,
            IBlockStatusService blockStatus,
            ILogger logger,
            string? path = null)
        {
            path ??= DefaultPath;
            var file = JsonSerializer.Deserialize<SeedFile>(await File.ReadAllTextAsync(path), JsonOptions)
                       ?? throw new InvalidOperationException($"Could not read {path}.");

            var destinations = await EnsureDestinationsAsync(context, file.Destinations);
            await EnsurePackagesAsync(context, codes, file.Packages, destinations);
            var nationality = await EnsureNationalityAsync(context, file.Defaults.Nationality);

            var packages = await context.Packages
                .Include(p => p.BaseDestination)
                .Include(p => p.Segments).ThenInclude(s => s.Destination)
                .ToListAsync();

            var importedNotes = await context.Bookings
                .Where(b => b.Notes != null && b.Notes.Contains("Imported from Notion"))
                .Select(b => b.Notes!)
                .ToListAsync();
            var existingNames = (await context.BookingGuests
                    .Where(g => g.IsLeadGuest)
                    .Select(g => g.FullName)
                    .ToListAsync())
                .Select(Normalize)
                .ToHashSet();

            var created = new List<int>();
            int duplicates = 0, failed = 0;

            foreach (var row in file.Bookings)
            {
                if (importedNotes.Any(n => n.Contains(row.NotionUrl)) || existingNames.Contains(Normalize(row.LeadGuestName)))
                {
                    duplicates++;
                    logger.LogInformation("Notion import: skipped {Name} ({Url}) — already in the app.", row.LeadGuestName, row.NotionUrl);
                    continue;
                }

                var package = row.PackageCode is not null
                    ? packages.FirstOrDefault(p => p.Code == row.PackageCode)
                    : packages.FirstOrDefault(p => string.Equals(p.Name, row.PackageName, StringComparison.OrdinalIgnoreCase));
                if (package is null)
                {
                    failed++;
                    logger.LogWarning("Notion import: no package {Package} for {Name}.", row.PackageCode ?? row.PackageName, row.LeadGuestName);
                    continue;
                }

                var booking = new Booking
                {
                    PackageId = package.Id,
                    ArrivalDate = row.ArrivalDate,
                    Category = file.Defaults.Category,
                    NationalityId = nationality.Id,
                    GuestEmail = file.Defaults.GuestEmail,
                    GuestPhone = file.Defaults.GuestPhone,
                    Status = row.Status,
                    Adults = row.Adults,
                    Notes = row.Notes,
                    CreatedAt = DateTime.UtcNow,
                };

                GeneratedItinerary itinerary;
                try
                {
                    itinerary = generator.Generate(package, row.ArrivalDate, null, null);
                }
                catch (InvalidOperationException ex)
                {
                    failed++;
                    logger.LogWarning("Notion import: could not schedule {Name} on {Package}: {Error}", row.LeadGuestName, package.Name, ex.Message);
                    continue;
                }

                booking.DepartureDate = itinerary.DepartureDate;
                booking.ExtraNights = itinerary.ExtraNights;
                booking.ExtensionReason = itinerary.ExtensionReason;
                booking.Stays = itinerary.Stays.OrderBy(s => s.SortOrder).Select(s => new BookingStay
                {
                    DestinationId = s.DestinationId,
                    IsCruise = s.IsCruise,
                    CheckInDate = s.CheckIn,
                    Nights = s.Nights,
                    BaselineNights = s.BaselineNights,
                    SortOrder = s.SortOrder,
                }).ToList();
                // WeekDays' bit layout is 1 << DayOfWeek — same derivation BookingController uses.
                var cruise = booking.Stays.FirstOrDefault(s => s.IsCruise);
                booking.EmbarkDay = cruise is null ? null : (WeekDays)(1 << (int)cruise.CheckInDate.DayOfWeek);

                booking.Guests.Add(new BookingGuest
                {
                    FullName = row.LeadGuestName,
                    Type = GuestType.Adult,
                    IsLeadGuest = true,
                    SortOrder = 0,
                });
                for (var i = 0; i < row.Rooms.Count; i++)
                    booking.Rooms.Add(new BookingRoom { Type = row.Rooms[i].Type, Quantity = row.Rooms[i].Quantity, SortOrder = i });

                await codes.AddWithCodeAsync(booking, CodePrefixes.Booking);

                // Written here rather than by the audit interceptor, which skips Added bookings.
                context.BookingAuditEvents.Add(new BookingAuditEvent
                {
                    BookingId = booking.Id,
                    OccurredAt = DateTime.UtcNow,
                    UserName = ImportedBy,
                    Action = AuditAction.Created,
                    Summary = $"Booking {booking.Code} imported from Notion",
                });
                await context.SaveChangesAsync();

                created.Add(booking.Id);
                existingNames.Add(Normalize(row.LeadGuestName));
            }

            if (created.Count > 0)
            {
                await blockStatus.RecalculateAsync(created);
                await context.SaveChangesAsync();
            }

            logger.LogInformation(
                "Notion import: {Created} created, {Duplicates} already present, {Failed} failed, {Skipped} left out by the export.",
                created.Count, duplicates, failed, file.Skipped.Count);
        }

        private static async Task<Dictionary<string, Destination>> EnsureDestinationsAsync(
            TravelContext context, List<DestinationSeed> seeds)
        {
            var all = await context.Destinations.ToListAsync();
            foreach (var seed in seeds.Where(s => !all.Any(d => Same(d.Name, s.Name))))
            {
                var destination = new Destination { Name = seed.Name, IsCruise = seed.IsCruise };
                context.Destinations.Add(destination);
                all.Add(destination);
            }
            await context.SaveChangesAsync();
            // Stored names carry trailing spaces ("Hurghada "), so lookups go through the trimmed key.
            return all.GroupBy(d => d.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                      .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }

        private static async Task EnsurePackagesAsync(
            TravelContext context, ICodeGeneratorService codes,
            List<PackageSeed> seeds, Dictionary<string, Destination> destinations)
        {
            var names = await context.Packages.Select(p => p.Name).ToListAsync();
            foreach (var seed in seeds.Where(s => !names.Any(n => Same(n, s.Name))))
            {
                var package = new Package
                {
                    Name = seed.Name,
                    BaseDestinationId = Find(destinations, seed.BaseDestination).Id,
                    TotalNights = seed.TotalNights,
                    BaseReturnNights = seed.BaseReturnNights,
                    Segments = seed.Segments.Select((s, i) => new PackageSegment
                    {
                        DestinationId = Find(destinations, s.Destination).Id,
                        Nights = s.Nights,
                        SortOrder = i,
                    }).ToList(),
                };
                await codes.AddWithCodeAsync(package, CodePrefixes.Package);
            }
        }

        private static async Task<Nationality> EnsureNationalityAsync(TravelContext context, string name)
        {
            var match = (await context.Nationalities.ToListAsync()).FirstOrDefault(n => Same(n.Name, name));
            if (match is not null)
                return match;

            match = new Nationality { Name = name };
            context.Nationalities.Add(match);
            await context.SaveChangesAsync();
            return match;
        }

        private static Destination Find(Dictionary<string, Destination> destinations, string name) =>
            destinations.TryGetValue(name.Trim(), out var d)
                ? d
                : throw new InvalidOperationException($"Destination '{name}' does not exist.");

        private static bool Same(string a, string b) =>
            string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

        // Case-, accent- and whitespace-insensitive, so "Júlia  Basini" matches "julia basini".
        private static string Normalize(string name)
        {
            var decomposed = name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var c in decomposed)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(char.IsWhiteSpace(c) ? ' ' : c);
            return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };

        private sealed class SeedFile
        {
            public SeedDefaults Defaults { get; set; } = new();
            public List<DestinationSeed> Destinations { get; set; } = new();
            public List<PackageSeed> Packages { get; set; } = new();
            public List<BookingSeed> Bookings { get; set; } = new();
            public List<JsonElement> Skipped { get; set; } = new();
        }

        private sealed class SeedDefaults
        {
            public PackageCategory Category { get; set; } = PackageCategory.FourStars;
            public string Nationality { get; set; } = "Brazilian";
            public string GuestEmail { get; set; } = string.Empty;
            public string GuestPhone { get; set; } = string.Empty;
        }

        private sealed class DestinationSeed
        {
            public string Name { get; set; } = string.Empty;
            public bool IsCruise { get; set; }
        }

        private sealed class PackageSeed
        {
            public string Name { get; set; } = string.Empty;
            public string BaseDestination { get; set; } = string.Empty;
            public int? TotalNights { get; set; }
            public int BaseReturnNights { get; set; } = 1;
            public List<SegmentSeed> Segments { get; set; } = new();
        }

        private sealed class SegmentSeed
        {
            public string Destination { get; set; } = string.Empty;
            public int Nights { get; set; }
        }

        private sealed class BookingSeed
        {
            public string NotionUrl { get; set; } = string.Empty;
            public string LeadGuestName { get; set; } = string.Empty;
            public string? PackageCode { get; set; }
            public string? PackageName { get; set; }
            public DateOnly ArrivalDate { get; set; }
            public int Adults { get; set; }
            public List<RoomSeed> Rooms { get; set; } = new();
            public BookingStatus Status { get; set; } = BookingStatus.InProgress;
            public string? Notes { get; set; }
        }

        private sealed class RoomSeed
        {
            public RoomType Type { get; set; }
            public int Quantity { get; set; }
        }
    }
}
