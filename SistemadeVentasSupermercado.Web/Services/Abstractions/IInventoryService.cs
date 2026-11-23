using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Web.Services.Abstractions
{
    public interface IInventoryService
    {
        /// <summary>Historial de movimientos (el más reciente primero) con filtros por producto, tipo y fechas.</summary>
        Task<Response<PaginationResponse<InventoryMovementDTO>>> GetMovementsAsync(InventoryQueryDTO query);

        /// <summary>Nombre, unidad y stock actual de un producto (cabecera del historial y formulario de ajuste).</summary>
        Task<Response<InventoryProductDTO>> GetProductAsync(Guid productId);

        /// <summary>
        /// Ajuste manual de stock (merma, pérdida, conteo físico...). Una salida solo se aplica si el stock
        /// alcanza; el usuario siempre es el de la sesión.
        /// </summary>
        Task<Response<AdjustInventoryResultDTO>> AdjustAsync(AdjustInventoryDTO dto, string userId);
    }
}
