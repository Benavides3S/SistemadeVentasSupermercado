using AutoMapper;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;

public class AutoMapperProfile : Profile
{
    public AutoMapperProfile()
    {
        CreateMap<Product, ProductDTO>().ReverseMap();
        CreateMap<Client, ClientDTO>().ReverseMap();
        CreateMap<User, AccountUserDTO>().ReverseMap();
        CreateMap<Permission, PermissionDTO>();

        CreateMap<SistemaVentasRole, SistemaVentaRoleDTO>().ReverseMap();
        CreateMap<CashRegister, CashRegisterDTO>()
           .ForMember(dest => dest.UserName, opt => opt.MapFrom(src => src.User.FirstName + " " + src.User.LastName))
           .ReverseMap();

        // Mapeo de User a UserDTO
        CreateMap<User, UserDTO>()
            .ForMember(dest => dest.SistemaVentasRoleId, opt => opt.MapFrom(src => src.SistemaVentasRoleId.ToString()));

        // Mapeo de UserDTO a User - SIMPLIFICADO
        CreateMap<UserDTO, User>()
            .ForMember(dest => dest.UserName, opt => opt.MapFrom(src => src.Email))
            .ForMember(dest => dest.SistemaVentasRoleId, opt => opt.Ignore()) // Lo manejamos en el servicio
            .ForMember(dest => dest.SistemaVentasRole, opt => opt.Ignore());
        CreateMap<PaymentMethod, PaymentMethodDTO>().ReverseMap();
        CreateMap<Discount, DiscountDTO>().ReverseMap();
    }
}
