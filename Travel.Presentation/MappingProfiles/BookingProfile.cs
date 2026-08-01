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
                .ForMember(d => d.LeadGuestIndex, o => o.MapFrom(s =>
                    s.Guests.OrderBy(x => x.SortOrder).ToList().FindIndex(g => g.IsLeadGuest)))
                .ForMember(d => d.PackageOptions, o => o.Ignore())
                .ForMember(d => d.StayDestinationOptions, o => o.Ignore())
                .ForMember(d => d.PackageHasFixedTotal, o => o.Ignore())
                .ForMember(d => d.PackageHasCruise, o => o.Ignore())
                .ForMember(d => d.PackageTotalNights, o => o.Ignore())
                .ForMember(d => d.PackageBaseDestinationId, o => o.Ignore());

            // DepartureDate/ExtraNights/ExtensionReason and the child collections are set by the
            // generator/controller. Code is issued once on create — ignoring it here is what stops an
            // edit (or a tampered POST) from overwriting an issued code.
            CreateMap<BookingViewModel, Booking>()
                .ForMember(d => d.Code, o => o.Ignore())
                .ForMember(d => d.Package, o => o.Ignore())
                .ForMember(d => d.DepartureDate, o => o.Ignore())
                .ForMember(d => d.ExtraNights, o => o.Ignore())
                .ForMember(d => d.ExtensionReason, o => o.Ignore())
                .ForMember(d => d.CreatedAt, o => o.Ignore())
                .ForMember(d => d.Stays, o => o.Ignore())
                .ForMember(d => d.Guests, o => o.Ignore())
                .ForMember(d => d.Rooms, o => o.Ignore());

            CreateMap<BookingStay, BookingStayInput>();

            // IsCruise is re-derived from the destination in the controller.
            CreateMap<BookingStayInput, BookingStay>()
                .ForMember(d => d.Id, o => o.Ignore())
                .ForMember(d => d.BookingId, o => o.Ignore())
                .ForMember(d => d.Booking, o => o.Ignore())
                .ForMember(d => d.Destination, o => o.Ignore())
                .ForMember(d => d.IsCruise, o => o.Ignore());

            CreateMap<BookingGuest, BookingGuestInput>();

            // IsLeadGuest comes from the form's single radio group, not from the row.
            CreateMap<BookingGuestInput, BookingGuest>()
                .ForMember(d => d.Id, o => o.Ignore())
                .ForMember(d => d.BookingId, o => o.Ignore())
                .ForMember(d => d.Booking, o => o.Ignore())
                .ForMember(d => d.IsLeadGuest, o => o.Ignore());

            CreateMap<BookingRoom, BookingRoomInput>();

            CreateMap<BookingRoomInput, BookingRoom>()
                .ForMember(d => d.Id, o => o.Ignore())
                .ForMember(d => d.BookingId, o => o.Ignore())
                .ForMember(d => d.Booking, o => o.Ignore());

            CreateMap<GeneratedStay, BookingStay>()
                .ForMember(d => d.CheckInDate, o => o.MapFrom(s => s.CheckIn))
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
