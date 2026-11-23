using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Web.Services.Abstractions
{
    public interface IHomeService
    {
        /// <summary>Resumen del Inicio: solo calcula lo que se pide (el controlador lo decide según los permisos).</summary>
        Task<Response<HomeSummaryDTO>> GetSummaryAsync(HomeRequestDTO request);
    }
}
