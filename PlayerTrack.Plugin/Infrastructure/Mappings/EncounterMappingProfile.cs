using AutoMapper;
using PlayerTrack.Models;

namespace PlayerTrack.Infrastructure;

public class EncounterMappingProfile : Profile
{
    public EncounterMappingProfile()
    {
        CreateMap<Encounter, EncounterDTO>()
            .ForMember(dest => dest.id, opt => opt.MapFrom(src => src.Id))
            .ForMember(dest => dest.created, opt => opt.MapFrom(src => src.Created))
            .ForMember(dest => dest.updated, opt => opt.MapFrom(src => src.Updated))
            .ForMember(dest => dest.territory_type_id, opt => opt.MapFrom(src => src.TerritoryTypeId))
            .ForMember(dest => dest.housing_ward, opt => opt.MapFrom(src => src.HousingWard))
            .ForMember(dest => dest.housing_plot, opt => opt.MapFrom(src => src.HousingPlot))
            .ForMember(dest => dest.housing_room, opt => opt.MapFrom(src => src.HousingRoom))
            .ForMember(dest => dest.housing_division, opt => opt.MapFrom(src => src.HousingDivision))
            .ForMember(dest => dest.ended, opt => opt.MapFrom(src => src.Ended));

        CreateMap<EncounterDTO, Encounter>()
            .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.id))
            .ForMember(dest => dest.Created, opt => opt.MapFrom(src => src.created))
            .ForMember(dest => dest.Updated, opt => opt.MapFrom(src => src.updated))
            .ForMember(dest => dest.TerritoryTypeId, opt => opt.MapFrom(src => src.territory_type_id))
            .ForMember(dest => dest.HousingWard, opt => opt.MapFrom(src => src.housing_ward))
            .ForMember(dest => dest.HousingPlot, opt => opt.MapFrom(src => src.housing_plot))
            .ForMember(dest => dest.HousingRoom, opt => opt.MapFrom(src => src.housing_room))
            .ForMember(dest => dest.HousingDivision, opt => opt.MapFrom(src => src.housing_division))
            .ForMember(dest => dest.Ended, opt => opt.MapFrom(src => src.ended));
    }
}
