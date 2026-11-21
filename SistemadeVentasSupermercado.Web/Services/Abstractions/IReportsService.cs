using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Web.Services.Abstractions
{
    public interface IReportsService
    {
        /// <summary>Ventas, devoluciones y neto por día (incluye los días sin movimiento).</summary>
        Task<Response<DailySalesReportDTO>> GetDailySalesAsync(DateOnly from, DateOnly to);

        /// <summary>Productos más vendidos en el rango, por dinero neto.</summary>
        Task<Response<List<TopProductDTO>>> GetTopProductsAsync(DateOnly from, DateOnly to, int top = 20);
    }
}
