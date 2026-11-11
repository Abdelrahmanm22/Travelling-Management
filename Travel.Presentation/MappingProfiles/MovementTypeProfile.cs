using AutoMapper;
using Travel.Domain.Models;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.MappingProfiles
{
    public class MovementTypeProfile : Profile
    {
        public MovementTypeProfile()
        {
            CreateMap<MovementType, MovementTypeViewModel>()
                .ForMember(d => d.IsFlight, o => o.Ignore())
                .ForMember(d => d.Scope, o => o.Ignore())
                .ForMember(d => d.Direction, o => o.Ignore())
                .ForMember(d => d.IsInUse, o => o.Ignore())
                .AfterMap((s, d) => d.SetLeg(s.FlightLeg));

            // FlightLeg is set by the controller, and only while no movement uses the type: changing it
            // would re-point every historical movement at a different flight.
            CreateMap<MovementTypeViewModel, MovementType>()
                .ForMember(d => d.FlightLeg, o => o.Ignore());
        }
    }
}
