using AutoMapper;
using Travel.Domain.Models;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.MappingProfiles
{
    public class TourProfile : Profile
    {
        public TourProfile()
        {
            CreateMap<Tour, TourViewModel>();
            CreateMap<TourViewModel, Tour>();
        }
    }
}
