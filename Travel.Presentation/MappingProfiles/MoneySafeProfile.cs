using AutoMapper;
using Travel.Domain.Models;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.MappingProfiles
{
    public class MoneySafeProfile : Profile
    {
        public MoneySafeProfile()
        {
            CreateMap<MoneySafe, MoneySafeViewModel>();

            // Currency is applied by the controller, not mapped: it is locked once the safe holds any
            // movement, so an ordinary Edit save — or a tampered POST — must not be able to set it.
            CreateMap<MoneySafeViewModel, MoneySafe>()
                .ForMember(d => d.Currency, o => o.Ignore())
                .ForMember(d => d.CreatedAt, o => o.Ignore())
                .ForMember(d => d.CreatedById, o => o.Ignore())
                .ForMember(d => d.CreatedByName, o => o.Ignore())
                .ForMember(d => d.Entries, o => o.Ignore());
        }
    }
}
