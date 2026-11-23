using SistemadeVentasSupermercado.Web.Data.Entities;

namespace SistemadeVentasSupermercado.Web.DTOs
{
    public class SaleLineDTO
    {
        public Guid ProductId { get; set; }
        public decimal Quantity { get; set; }
    }

    public class CreateSaleDTO
    {
        public Guid? ClientId { get; set; }
        public PaymentMethod PaymentMethod { get; set; }
        public List<SaleLineDTO> Items { get; set; } = new();

        /// <summary>
        /// Código único que genera el punto de venta para cada cobro. Si llega dos veces
        /// (reintento tras un corte de red), la venta no se registra de nuevo.
        /// </summary>
        public Guid IdempotencyKey { get; set; }
    }

    public class SaleResultDTO
    {
        public Guid Id { get; set; }
        public int Number { get; set; }
        public decimal Total { get; set; }

        /// <summary>Verdadero si el cobro era un reintento y la venta ya estaba registrada.</summary>
        public bool AlreadyRegistered { get; set; }
    }
}