using AutoMapper;
using Travel.Domain.Enums;
using Travel.Domain.Models;
using Travel.Presentation.ViewModels;

namespace Travel.Presentation.MappingProfiles
{
    public class DestinationProfile : Profile
    {
        public DestinationProfile()
        {
            CreateMap<Destination, DestinationViewModel>()
                .ForMember(d => d.SelectedDepartureDays, o => o.MapFrom(s => ToList(s.DepartureDays)));

            CreateMap<DestinationViewModel, Destination>()
                .ForMember(d => d.DepartureDays, o => o.MapFrom(s => ToBitmask(s.IsCruise, s.SelectedDepartureDays)))
                .ForMember(d => d.Segments, o => o.Ignore());
        }

        // Bitmask → list of set days (empty when null).
        // Public so AutoMapper's compiled mapping delegate can call it without access issues.
        public static List<WeekDays> ToList(WeekDays? mask) =>
            mask is null
                ? new List<WeekDays>()
                : Enum.GetValues<WeekDays>().Where(d => mask.Value.HasFlag(d)).ToList();

        // Selected days → bitmask; null when not a cruise or nothing selected.
        public static WeekDays? ToBitmask(bool isCruise, List<WeekDays> days)
        {
            if (!isCruise || days is null || days.Count == 0)
                return null;

            WeekDays result = 0;
            foreach (var d in days)
                result |= d;
            return result;
        }
    }
}
