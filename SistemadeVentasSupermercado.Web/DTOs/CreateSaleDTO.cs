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
    }

    public class SaleResultDTO
    {
        public Guid Id { get; set; }
        public int Number { get; set; }
        public decimal Total { get; set; }
    }
}