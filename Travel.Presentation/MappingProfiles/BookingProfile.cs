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
                .ForMember(d => d.PackageOptions, o => o.Ignore())
                .ForMember(d => d.StayDestinationOptions, o => o.Ignore())
                .ForMember(d => d.PackageHasFixedTotal, o => o.Ignore())
                .ForMember(d => d.PackageHasCruise, o => o.Ignore())
                .ForMember(d => d.PackageTotalNights, o => o.Ignore())
                .ForMember(d => d.PackageBaseDestinationId, o => o.Ignore());

            // DepartureDate/ExtraNights/ExtensionReason/Stays are set by the generator/controller.
            CreateMap<BookingViewModel, Booking>()
                .ForMember(d => d.Package, o => o.Ignore())
                .ForMember(d => d.DepartureDate, o => o.Ignore())
                .ForMember(d => d.ExtraNights, o => o.Ignore())
                .ForMember(d => d.ExtensionReason, o => o.Ignore())
                .ForMember(d => d.CreatedAt, o => o.Ignore())
                .ForMember(d => d.Stays, o => o.Ignore());

            CreateMap<BookingStay, BookingStayInput>();

            // IsCruise is re-derived from the destination in the controller.
            CreateMap<BookingStayInput, BookingStay>()
                .ForMember(d => d.Id, o => o.Ignore())
                .ForMember(d => d.BookingId, o => o.Ignore())
                .ForMember(d => d.Booking, o => o.Ignore())
                .ForMember(d => d.Destination, o => o.Ignore())
                .ForMember(d => d.IsCruise, o => o.Ignore());

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
