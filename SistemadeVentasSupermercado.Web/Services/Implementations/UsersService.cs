using AutoMapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;
using System.Security.Claims;

namespace SistemadeVentasSupermercado.Web.Services.Implementations
{
    // TODO: Mejorar mensajes de error
    public class UsersService : CustomQueryableOperationsService, IUsersService
    {
        private readonly DataContext _context;
        private readonly SignInManager<User> _signInManager;
        private readonly UserManager<User> _userManager;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IMapper _mapper;

        public UsersService(DataContext context,
                            SignInManager<User> signInManager,
                            UserManager<User> userManager,
                            IHttpContextAccessor httpContextAccessor,
                            IMapper mapper) : base(context, mapper)
        {
            _context = context;
            _signInManager = signInManager;
            _userManager = userManager;
            _httpContextAccessor = httpContextAccessor;
            _mapper = mapper;
        }

        public async Task<Response<IdentityResult>> AddUserAsync(User user, string password)
        {
            IdentityResult result = await _userManager.CreateAsync(user, password);

            return new Response<IdentityResult>
            {
                Result = result,
                IsSuccess = result.Succeeded,
            };
        }

        public async Task<Response<IdentityResult>> ConfirmUserAsync(User user, string token)
        {
            IdentityResult result = await _userManager.ConfirmEmailAsync(user, token);

            return new Response<IdentityResult>
            {
                Result = result,
                IsSuccess = result.Succeeded,
            };
        }

        public async Task<Response<UserDTO>> CreateAsync(UserDTO dto)
        {
            try
            {
                User user = _mapper.Map<User>(dto);
                // Id is a Guid (IdentityUser<Guid>)
                user.Id = Guid.NewGuid().ToString();
                // Use document as initial password as previous behavior (consider changing)
                Response<IdentityResult> createResponse = await AddUserAsync(user, dto.Document);

                if (createResponse is null || !createResponse.IsSuccess || createResponse.Result == null)
                {
                    var errors = createResponse?.Result?.Errors.Select(e => e.Description).ToList() ?? new List<string> { "No se pudo crear el usuario." };
                    return Response<UserDTO>.Failure("No se pudo crear el usuario.", errors);
                }

                // TODO: Envío de email para confirmación
                Response<string> tokenResponse = await GenerateConfirmationTokenAsync(user);

                if (tokenResponse == null || !tokenResponse.IsSuccess || string.IsNullOrWhiteSpace(tokenResponse.Result))
                {
                    return Response<UserDTO>.Failure("No se pudo generar el token de confirmación.");
                }

                Response<IdentityResult> confirmResponse = await ConfirmUserAsync(user, tokenResponse.Result);

                if (confirmResponse == null || !confirmResponse.IsSuccess)
                {
                    var errors = confirmResponse?.Result?.Errors.Select(e => e.Description).ToList() ?? new List<string> { "No se pudo confirmar el email del usuario." };
                    return Response<UserDTO>.Failure("No se pudo confirmar el email del usuario.", errors);
                }

                return Response<UserDTO>.Success(_mapper.Map<UserDTO>(user), "Usuario creado con éxito");
            }
            catch (Exception ex)
            {
                return Response<UserDTO>.Failure(ex);
            }
        }

        public bool CurrentUserIsAuthenticaded()
        {
            ClaimsPrincipal? user = _httpContextAccessor.HttpContext?.User;
            return user?.Identity is not null && user.Identity.IsAuthenticated;
        }

        public async Task<bool> CurrentUserIsAuthorizedAsync(string permission, string module)
        {
            ClaimsPrincipal? claimsUser = _httpContextAccessor.HttpContext?.User;

            // Valida si hay sesión
            if (claimsUser is null)
            {
                return false;
            }

            string userName = claimsUser.Identity!.Name!;

            User? user = await GetUserByEmailAsync(userName);

            if (user is null)
            {
                return false;
            }

            // Avoid dereference of possibly null navigation property
            if (user.SistemaVentasRole?.Name == Env.SUPER_ADMIN_ROLE_NAME)
            {
                return true;
            }

            // Ensure RolePermissions is not null before using Any
            return await _context.Permissions.Include(p => p.RolePermissions)
                                             .AnyAsync(p => (p.Module == module && p.Name == permission)
                                                            && p.RolePermissions != null
                                                            && p.RolePermissions.Any(rp => rp.SistemaVentasRoleId == user.SistemaVentasRoleId));
        }

        //public Task<Response<object>> DeleteAsync(Guid id)
        //{
        //    throw new NotImplementedException();
        //}

        public async Task<Response<UserDTO>> EditAsync(UserDTO dto)
        {
            try
            {
                User? user = await GetUserAsync(dto.Id?.ToString());

                if (user == null)
                {
                    return Response<UserDTO>.Failure("No existe usuario.");
                }

                user.PhoneNumber = dto.PhoneNumber;
                user.Document = dto.Document;
                user.FirstName = dto.FirstName;
                user.LastName = dto.LastName;

                // dto.SistemaVentasRoleId is expected to be a string representation of a Guid
                if (!string.IsNullOrWhiteSpace(dto.SistemaVentasRoleId) && Guid.TryParse(dto.SistemaVentasRoleId, out var roleGuid))
                {
                    user.SistemaVentasRoleId = roleGuid;
                }

                _context.Users.Update(user);

                await _context.SaveChangesAsync();

                return Response<UserDTO>.Success(dto, "Usuario actualizado con éxito");
            }
            catch (Exception ex)
            {
                return Response<UserDTO>.Failure(ex);
            }
        }

        public async Task<Response<string>> GenerateConfirmationTokenAsync(User user)
        {
            string result = await _userManager.GeneratePasswordResetTokenAsync(user);

            return Response<string>.Success(result);
        }

        //public Task<Response<UserDTO>> GetOneAsync(Guid id)
        //{
        //    throw new NotImplementedException();
        //}

        public async Task<Response<PaginationResponse<UserDTO>>> GetPaginatedListAsync(PaginationRequest request)
        {
            IQueryable<User> queryable = _context.Users.AsQueryable();

            if (!string.IsNullOrEmpty(request.Filter))
            {
                string f = request.Filter.ToLowerInvariant();

                queryable = queryable.Where(u =>
                       (!string.IsNullOrEmpty(u.FirstName) && u.FirstName.ToLower().Contains(f))
                    || (!string.IsNullOrEmpty(u.LastName) && u.LastName.ToLower().Contains(f))
                    || (!string.IsNullOrEmpty(u.Document) && u.Document.ToLower().Contains(f))
                    || (!string.IsNullOrEmpty(u.Email) && u.Email.ToLower().Contains(f))
                    || (!string.IsNullOrEmpty(u.PhoneNumber) && u.PhoneNumber.ToLower().Contains(f))
                );
            }

            return await GetPaginationAsync<User, UserDTO>(request, queryable);
        }

        public async Task<User?> GetUserByEmailAsync(string email)
        {
            return await _context.Users.Include(u => u.SistemaVentasRole)
                                       .FirstOrDefaultAsync(u => u.Email == email);
        }

        public async Task<User?> GetUserByIdAsync(Guid id)
        {
            return await _context.Users.Include(u => u.SistemaVentasRole)
                                       .FirstOrDefaultAsync(u => u.Id == id.ToString());
        }

        public async Task<Response<SignInResult>> LoginAsync(LoginDTO dto)
        {
            SignInResult result = await _signInManager.PasswordSignInAsync(dto.Email, dto.Password, false, false);

            return new Response<SignInResult>
            {
                Result = result,
                IsSuccess = result.Succeeded,
            };
        }

        public async Task LogoutAsync()
        {
            await _signInManager.SignOutAsync();
        }

        public async Task<Response<AccountUserDTO>> UpdateUserAsync(AccountUserDTO dto)
        {
            try
            {
                User? user = await GetUserAsync(dto.Id);

                if (user == null)
                {
                    return Response<AccountUserDTO>.Failure("No existe usuario.");
                }

                user.PhoneNumber = dto.PhoneNumber;
                user.Document = dto.Document;
                user.FirstName = dto.FirstName;
                user.LastName = dto.LastName;

                _context.Users.Update(user);

                await _context.SaveChangesAsync();

                return Response<AccountUserDTO>.Success(dto, "Datos actualizados con éxito");
            }
            catch (Exception ex)
            {
                return Response<AccountUserDTO>.Failure(ex);
            }
        }

        private async Task<User?> GetUserAsync(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            if (!Guid.TryParse(id, out var guid))
            {
                return null;
            }

            // FindAsync with a Guid key
            var user = await _context.Users.FindAsync(guid);
            return user;
        }
    }
}
