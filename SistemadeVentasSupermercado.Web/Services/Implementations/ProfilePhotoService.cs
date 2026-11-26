using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Services.Implementations
{
    public class ProfilePhotoService : IProfilePhotoService
    {
        private readonly DataContext _context;
        private readonly IUserActivityRecorder _activity;

        public ProfilePhotoService(DataContext context, IUserActivityRecorder activity)
        {
            _context = context;
            _activity = activity;
        }

        public Task<byte[]?> GetAsync(string userName)
            => _context.UserPhotos.AsNoTracking()
                .Where(p => p.User!.UserName == userName)
                .Select(p => p.Content)
                .FirstOrDefaultAsync()!;

        public async Task<long?> GetVersionAsync(string userName)
        {
            DateTime? updated = await _context.UserPhotos
                .Where(p => p.User!.UserName == userName)
                .Select(p => (DateTime?)p.UpdatedAtUtc)
                .FirstOrDefaultAsync();

            return updated?.Ticks;
        }

        public async Task<Response<bool>> SaveAsync(string userName, byte[] content)
        {
            string? problem = ProfilePhotoRules.Validate(content);
            if (problem is not null)
                return Response<bool>.Failure(problem);

            try
            {
                User? user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserName == userName);
                if (user is null)
                    return Response<bool>.Failure("No existe el usuario.");

                UserPhoto? photo = await _context.UserPhotos.FindAsync(user.Id);
                if (photo is null)
                {
                    photo = new UserPhoto { UserId = user.Id };
                    _context.UserPhotos.Add(photo);
                }

                photo.Content = content;
                photo.UpdatedAtUtc = DateTime.UtcNow;

                // Un solo SaveChanges: la foto y su registro en el historial quedan juntos o no queda nada.
                await _activity.StageAsync(UserActivityType.ProfileUpdated, userName, user,
                    details: "Cambió: Foto de perfil");
                await _context.SaveChangesAsync();

                return Response<bool>.Success(true, "Foto guardada.");
            }
            catch (Exception ex)
            {
                return Response<bool>.Failure(ex, "No se pudo guardar la foto.");
            }
        }

        public async Task<Response<bool>> RemoveAsync(string userName)
        {
            try
            {
                User? user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserName == userName);
                if (user is null)
                    return Response<bool>.Failure("No existe el usuario.");

                UserPhoto? photo = await _context.UserPhotos.FindAsync(user.Id);
                if (photo is not null)
                {
                    _context.UserPhotos.Remove(photo);
                    await _activity.StageAsync(UserActivityType.ProfileUpdated, userName, user,
                        details: "Quitó la foto de perfil");
                    await _context.SaveChangesAsync();
                }

                return Response<bool>.Success(true, "Foto eliminada.");
            }
            catch (Exception ex)
            {
                return Response<bool>.Failure(ex, "No se pudo quitar la foto.");
            }
        }
    }
}
