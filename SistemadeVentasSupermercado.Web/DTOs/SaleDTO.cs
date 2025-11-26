using SistemadeVentasSupermercado.Web.Data.Entities;
using System.ComponentModel.DataAnnotations;

namespace SistemadeVentasSupermercado.Web.DTOs
{
    public class SaleDTO
    {
        public Guid Id { get; set; }

        [Display(Name = "Número de Venta")]
        public string SaleNumber { get; set; } = string.Empty;

        [Display(Name = "Fecha Venta")]
        public DateTime SaleDate { get; set; } = DateTime.Now;

        [Display(Name = "Caja")]
        public Guid CashRegisterId { get; set; }

        [Display(Name = "Cliente")]
        public Guid? ClientId { get; set; }

        [Display(Name = "Nombre Cliente")]
        public string? ClientName { get; set; }

        [Display(Name = "Usuario")]
        public Guid UserId { get; set; }

        [Display(Name = "Nombre Usuario")]
        public string UserName { get; set; } = string.Empty;

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Range(0, double.MaxValue, ErrorMessage = "El subtotal debe ser mayor o igual a 0")]
        [Display(Name = "Subtotal")]
        public decimal Subtotal { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "El descuento debe ser mayor o igual a 0")]
        [Display(Name = "Descuento")]
        public decimal DiscountAmount { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Range(0.01, double.MaxValue, ErrorMessage = "El total debe ser mayor a 0")]
        [Display(Name = "Total")]
        public decimal TotalAmount { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Display(Name = "Método de Pago")]
        public string PaymentMethod { get; set; } = string.Empty;

        [Display(Name = "Estado")]
        public SaleStatus Status { get; set; }

        [Display(Name = "Observaciones")]
        public string? Observations { get; set; }

        // Detalles de la venta
        public List<SaleDetailDTO> SaleDetails { get; set; } = new List<SaleDetailDTO>();
    }

    public class SaleDetailDTO
    {
        public Guid Id { get; set; }
        public Guid SaleId { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        public Guid ProductId { get; set; }

        [Display(Name = "Producto")]
        public string ProductName { get; set; } = string.Empty;

        [Display(Name = "Precio")]
        public decimal ProductPrice { get; set; }

        [Display(Name = "Stock")]
        public int ProductStock { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Range(1, int.MaxValue, ErrorMessage = "La cantidad debe ser mayor a 0")]
        [Display(Name = "Cantidad")]
        public int Quantity { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Range(0, double.MaxValue, ErrorMessage = "El precio debe ser mayor o igual a 0")]
        [Display(Name = "Precio Unitario")]
        public decimal UnitPrice { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "El descuento debe ser mayor o igual a 0")]
        [Display(Name = "Descuento")]
        public decimal Discount { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Range(0.01, double.MaxValue, ErrorMessage = "El total debe ser mayor a 0")]
        [Display(Name = "Total")]
        public decimal Total { get; set; }
    }

    public class CreateSaleDTO
    {
        [Display(Name = "Cliente")]
        public Guid? ClientId { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Display(Name = "Método de Pago")]
        public string PaymentMethod { get; set; } = string.Empty;

        [Display(Name = "Observaciones")]
        public string? Observations { get; set; }

        // Descuento general aplicado a la venta
        [Range(0, 100, ErrorMessage = "El descuento debe estar entre 0 y 100")]
        [Display(Name = "Descuento General (%)")]
        public decimal GeneralDiscountPercent { get; set; }

        // Detalles de la venta
        public List<SaleDetailDTO> SaleDetails { get; set; } = new List<SaleDetailDTO>();
    }

    public class CancelSaleDTO
    {
        public Guid SaleId { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Display(Name = "Motivo de Anulación")]
        public string CancellationReason { get; set; } = string.Empty;
    }
}
