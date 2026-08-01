using AutoMapper;
using Travel.Domain.Models;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.MappingProfiles
{
    public class PackageProfile : Profile
    {
        public PackageProfile()
        {
            CreateMap<Package, PackageViewModel>()
                .ForMember(d => d.ExistingImageUrl, o => o.MapFrom(s => s.ImageUrl))
                .ForMember(d => d.Segments, o => o.MapFrom(s => s.Segments.OrderBy(x => x.SortOrder)))
                .ForMember(d => d.CoverImage, o => o.Ignore())
                .ForMember(d => d.BaseDestinationOptions, o => o.Ignore())
                .ForMember(d => d.SegmentDestinationOptions, o => o.Ignore());

            // ImageUrl (set after upload) and Segments (built with SortOrder) are handled in the controller.
            CreateMap<PackageViewModel, Package>()
                .ForMember(d => d.Code, o => o.Ignore())
                .ForMember(d => d.ImageUrl, o => o.Ignore())
                .ForMember(d => d.BaseDestination, o => o.Ignore())
                .ForMember(d => d.CreatedAt, o => o.Ignore())
                .ForMember(d => d.Segments, o => o.Ignore());

            CreateMap<PackageSegment, PackageSegmentInput>();
        }
    }
}
