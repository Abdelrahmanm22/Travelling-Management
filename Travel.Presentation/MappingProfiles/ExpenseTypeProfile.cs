using AutoMapper;
using Travel.Domain.Models;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.MappingProfiles
{
    public class ExpenseTypeProfile : Profile
    {
        public ExpenseTypeProfile()
        {
            CreateMap<ExpenseType, ExpenseTypeViewModel>().ReverseMap();
        }
    }
}
