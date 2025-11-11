using AutoMapper;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Web.Core
{
    public class AutoMapperProfile : Profile
    {
        public AutoMapperProfile()
        {
            CreateMap<Product, ProductDTO>().ReverseMap();
            CreateMap<Client, ClientDTO>().ReverseMap();
            CreateMap<User, AccountUserDTO>().ReverseMap();
            CreateMap<Permission, PermissionDTO>();

            CreateMap<SistemaVentasRole, SistemaVentaRoleDTO>().ReverseMap();
            CreateMap<User, UserDTO>();

            CreateMap<UserDTO, User>().ForMember(user => user.UserName, config => config.MapFrom(dto => dto.Email));
        }
    }
}
