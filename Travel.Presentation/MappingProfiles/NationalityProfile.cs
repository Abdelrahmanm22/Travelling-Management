using AutoMapper;
using Travel.Domain.Models;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.MappingProfiles
{
    public class NationalityProfile : Profile
    {
        public NationalityProfile()
        {
            CreateMap<Nationality, NationalityViewModel>();
            CreateMap<NationalityViewModel, Nationality>();
        }
    }
}
