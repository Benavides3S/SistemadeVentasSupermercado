using SistemadeVentasSupermercado.Web.Core;

namespace SistemadeVentasSupermercado.Web.Services.Abstractions
{
    /// <summary>
    /// Foto de perfil. userName es SIEMPRE el de la sesión: cada quien ve y cambia solo la suya.
    /// </summary>
    public interface IProfilePhotoService
    {
        /// <summary>La imagen (JPEG), o null si no tiene.</summary>
        Task<byte[]?> GetAsync(string userName);

        /// <summary>Un número que cambia con cada foto, o null si no tiene. No carga la imagen.</summary>
        Task<long?> GetVersionAsync(string userName);

        Task<Response<bool>> SaveAsync(string userName, byte[] content);

        Task<Response<bool>> RemoveAsync(string userName);
    }
}
