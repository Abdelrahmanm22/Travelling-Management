using AutoMapper;
using Travel.Domain.Models;
using Travel.Domain.Services.Itinerary;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.MappingProfiles
{
    public class BookingProfile : Profile
    {
        public BookingProfile()
        {
            CreateMap<Booking, BookingViewModel>()
                .ForMember(d => d.Stays, o => o.MapFrom(s => s.Stays.OrderBy(x => x.SortOrder)))
                .ForMember(d => d.Guests, o => o.MapFrom(s => s.Guests.OrderBy(x => x.SortOrder)))
                .ForMember(d => d.Rooms, o => o.MapFrom(s => s.Rooms.OrderBy(x => x.SortOrder)))
                .ForMember(d => d.Tours, o => o.MapFrom(s => s.Tours.OrderBy(x => x.SortOrder)))
                .ForMember(d => d.LeadGuestIndex, o => o.MapFrom(s =>
                    s.Guests.OrderBy(x => x.SortOrder).ToList().FindIndex(g => g.IsLeadGuest)))
                .ForMember(d => d.CanEditPricing, o => o.Ignore())
                .ForMember(d => d.CanViewPricing, o => o.Ignore())
                .ForMember(d => d.CanCreateTransactions, o => o.Ignore())
                // Create-only form state with no entity counterpart.
                .ForMember(d => d.FirstPaymentAmount, o => o.Ignore())
                .ForMember(d => d.FirstPaymentMethod, o => o.Ignore())
                .ForMember(d => d.FirstPaymentDate, o => o.Ignore())
                .ForMember(d => d.FirstPaymentReference, o => o.Ignore())
                .ForMember(d => d.FirstPaymentNotes, o => o.Ignore())
                .ForMember(d => d.FirstPaymentFiles, o => o.Ignore())
                .ForMember(d => d.PackageOptions, o => o.Ignore())
                .ForMember(d => d.StayDestinationOptions, o => o.Ignore())
                .ForMember(d => d.NationalityOptions, o => o.Ignore())
                .ForMember(d => d.TourOptions, o => o.Ignore())
                .ForMember(d => d.PackageHasFixedTotal, o => o.Ignore())
                .ForMember(d => d.PackageHasCruise, o => o.Ignore())
                .ForMember(d => d.PackageTotalNights, o => o.Ignore())
                .ForMember(d => d.PackageBaseDestinationId, o => o.Ignore())
                .ForMember(d => d.EmbarkOptions, o => o.Ignore())
                // Attachments are file uploads / a controller-built display list — populated manually, not mapped.
                .ForMember(d => d.FlightFiles, o => o.Ignore())
                .ForMember(d => d.PassportFiles, o => o.Ignore())
                .ForMember(d => d.ContractLink, o => o.Ignore())
                .ForMember(d => d.RemoveAttachmentIds, o => o.Ignore())
                .ForMember(d => d.ExistingAttachments, o => o.Ignore());

            // DepartureDate/ExtraNights/ExtensionReason and the child collections are set by the
            // generator/controller. Code is issued once on create — ignoring it here is what stops an
            // edit (or a tampered POST) from overwriting an issued code. EmbarkDay is likewise set by
            // the controller: only Create posts it, so mapping it would null the stored value on Edit.
            // FlightArrivalDate/FlightDepartureDate deliberately are NOT in this list — unlike
            // DepartureDate they're pure operator input, so they must flow through the map.
            // Status is owned by Block Services (In progress/Confirmed are derived from the booking's
            // blocks) and by the SetStatus action (Completed/Cancelled); ignoring it here keeps an
            // ordinary Edit save — or a tampered POST — from overwriting it, same as Code.
            // The three prices are ignored for the same reason, and it is the load-bearing half of the
            // Bookings.EditPricing gate: the controller assigns them explicitly only when the user holds
            // that permission, so a POST from someone who can edit a booking but not price it leaves the
            // stored figures alone instead of nulling them.
            CreateMap<BookingViewModel, Booking>()
                .ForMember(d => d.Code, o => o.Ignore())
                .ForMember(d => d.Status, o => o.Ignore())
                .ForMember(d => d.AdultPrice, o => o.Ignore())
                .ForMember(d => d.ChildPrice, o => o.Ignore())
                .ForMember(d => d.InfantPrice, o => o.Ignore())
                .ForMember(d => d.Package, o => o.Ignore())
                // NationalityId flows through; the navigation must not be touched by the map.
                .ForMember(d => d.Nationality, o => o.Ignore())
                .ForMember(d => d.EmbarkDay, o => o.Ignore())
                .ForMember(d => d.DepartureDate, o => o.Ignore())
                .ForMember(d => d.ExtraNights, o => o.Ignore())
                .ForMember(d => d.ExtensionReason, o => o.Ignore())
                .ForMember(d => d.CreatedAt, o => o.Ignore())
                .ForMember(d => d.Stays, o => o.Ignore())
                .ForMember(d => d.Guests, o => o.Ignore())
                .ForMember(d => d.Rooms, o => o.Ignore())
                .ForMember(d => d.Tours, o => o.Ignore())
                .ForMember(d => d.Transactions, o => o.Ignore())
                .ForMember(d => d.AuditEvents, o => o.Ignore());

            // Rates map entity → input directly; the reverse is built by hand in
            // BuildStaysFromInputsAsync, which drops the rows at quantity 0 and the unentered rates.
            CreateMap<BookingStay, BookingStayInput>()
                .ForMember(d => d.Rates, o => o.MapFrom(s => s.Rates.OrderBy(r => r.RoomType)));

            CreateMap<BookingStayRate, BookingStayRateInput>();

            // IsCruise is re-derived from the destination in the controller. Rates are ignored for
            // the same reason the booking's prices are: the controller writes them only when the
            // user holds Bookings.EditPricing, so an ordinary save can't wipe what it never rendered.
            CreateMap<BookingStayInput, BookingStay>()
                .ForMember(d => d.Id, o => o.Ignore())
                .ForMember(d => d.BookingId, o => o.Ignore())
                .ForMember(d => d.Booking, o => o.Ignore())
                .ForMember(d => d.Destination, o => o.Ignore())
                .ForMember(d => d.Rates, o => o.Ignore())
                .ForMember(d => d.ExtraNightsArrangedByGuest, o => o.Ignore())
                .ForMember(d => d.IsCruise, o => o.Ignore());

            CreateMap<BookingGuest, BookingGuestInput>();

            // IsLeadGuest comes from the form's single radio group, not from the row.
            CreateMap<BookingGuestInput, BookingGuest>()
                .ForMember(d => d.Id, o => o.Ignore())
                .ForMember(d => d.BookingId, o => o.Ignore())
                .ForMember(d => d.Booking, o => o.Ignore())
                .ForMember(d => d.IsLeadGuest, o => o.Ignore());

            // A guest's SortOrder is its index in Guests (BuildGuests sets it so), which is exactly what
            // the tour checkboxes post back.
            CreateMap<BookingTour, BookingTourInput>()
                .ForMember(d => d.GuestIndexes, o => o.MapFrom(s =>
                    s.Guests.Select(g => g.Guest.SortOrder).OrderBy(x => x).ToList()))
                .ForMember(d => d.LegacyPax, o => o.MapFrom(s => s.Guests.Any() ? (int?)null : s.Pax));

            CreateMap<BookingRoom, BookingRoomInput>();

            CreateMap<BookingRoomInput, BookingRoom>()
                .ForMember(d => d.Id, o => o.Ignore())
                .ForMember(d => d.BookingId, o => o.Ignore())
                .ForMember(d => d.Booking, o => o.Ignore());

            // BaselineNights flows through both ways: the generator sets it, and the form posts it back
            // unchanged. It is never derived from Nights — that's the whole point of it.
            CreateMap<GeneratedStay, BookingStay>()
                .ForMember(d => d.CheckInDate, o => o.MapFrom(s => s.CheckIn))
                // A regenerated schedule is a different schedule: it inherits neither the rates nor
                // the guest-arranged flag of the rows it replaces.
                .ForMember(d => d.Rates, o => o.Ignore())
                .ForMember(d => d.ExtraNightsArrangedByGuest, o => o.Ignore())
                .ForMember(d => d.Id, o => o.Ignore())
                .ForMember(d => d.BookingId, o => o.Ignore())
                .ForMember(d => d.Booking, o => o.Ignore())
                .ForMember(d => d.Destination, o => o.Ignore())
                .ForMember(d => d.IsOverridden, o => o.Ignore())
                .ForMember(d => d.HotelName, o => o.Ignore())
                .ForMember(d => d.Notes, o => o.Ignore());
        }
    }
}
