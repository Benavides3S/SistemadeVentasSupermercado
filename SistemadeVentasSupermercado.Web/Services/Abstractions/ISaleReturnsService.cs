using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Web.Services.Abstractions
{
    public interface ISaleReturnsService
    {
        /// <summary>Datos para el formulario: cuánto queda por devolver de cada línea.</summary>
        Task<Response<ReturnableSaleDTO>> GetReturnableAsync(Guid saleId);

        /// <summary>
        /// Registra una devolución parcial o total. Reembolsa al precio original y con el mismo
        /// método de pago; si es en efectivo, sale del turno abierto de quien devuelve.
        /// authorizedByUserId: el supervisor cuyo PIN YA se comprobó (null si quien devuelve tiene el permiso).
        /// Nunca sale de un dato del navegador sin comprobar.
        /// </summary>
        Task<Response<SaleReturnDetailDTO>> CreateAsync(Guid saleId, CreateSaleReturnDTO dto, string userId,
            string? authorizedByUserId = null);

        Task<Response<SaleReturnDetailDTO>> GetOneAsync(Guid id);
    }
}
