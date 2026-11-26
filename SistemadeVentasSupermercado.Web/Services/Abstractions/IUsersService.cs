using Microsoft.AspNetCore.Identity;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Web.Services.Abstractions
{
    public interface IUsersService
    {
        public Task<Response<IdentityResult>> AddUserAsync(User user, string password);
        public Task<Response<IdentityResult>> ConfirmUserAsync(User user, string token);
        public bool CurrentUserIsAuthenticaded();
        public Task<bool> CurrentUserIsAuthorizedAsync(string permission, string module);
        public Task<Response<string>> GenerateConfirmationTokenAsync(User user);
      
        public Task<User> GetUserByEmailAsync(string email);
        public Task<User> GetUserByIdAsync(Guid id);
        public Task<Response<SignInResult>> LoginAsync(LoginDTO dto);
        public Task LogoutAsync();
        /// <summary>El formulario de ingreso se rechazó antes de revisar la contraseña (el correo no es válido).</summary>
        public Task RecordRejectedLoginFormAsync(LoginDTO dto);
        public Task<Response<AccountUserDTO>> UpdateUserAsync(AccountUserDTO dto, string userName);
        // For Management
        public Task<Response<TemporaryPasswordDTO>> CreateAsync(UserDTO dto, string requestedByUserName);
        public Task<Response<TemporaryPasswordDTO>> ResetPasswordAsync(Guid userId, string requestedByUserName);
        public Task<Response<User>> ChangePasswordAsync(string userName, ChangePasswordDTO dto);
        public Task RefreshSignInAsync(User user);
        public Task<bool> MustChangePasswordAsync(string userName);

        /// <summary>Indica si esa es la contraseña actual del usuario (no cuenta como intento fallido).</summary>
        public Task<bool> CheckPasswordAsync(User user, string password);
        //public Task<Response<object>> DeleteAsync(Guid id);
        public Task<Response<UserDTO>> EditAsync(UserDTO dto, string requestedByUserName);
        //public Task<Response<UserDTO>> GetOneAsync(Guid id);
        public Task<Response<PaginationResponse<UserDTO>>> GetPaginatedListAsync(PaginationRequest request, UserStatus? status = null);
        public Task<Response<UserDeactivationDTO>> DeactivateAsync(Guid userId, string requestedByUserName);
        public Task<Response<string>> ReactivateAsync(Guid userId, string requestedByUserName);
        public Task<Response<string>> UnlockAsync(Guid userId, string requestedByUserName);
    }
}
