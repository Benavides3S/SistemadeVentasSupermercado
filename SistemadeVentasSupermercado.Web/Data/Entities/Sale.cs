using SistemadeVentasSupermercado.Web.Data.Abstractions;
using System.ComponentModel.DataAnnotations;

namespace SistemadeVentasSupermercado.Web.Data.Entities
{
    public class Sale : IId
    {
        [Key]
        public Guid Id { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Display(Name = "Número de Venta")]
        public string SaleNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Display(Name = "Fecha Venta")]
        public DateTime SaleDate { get; set; } = DateTime.Now;

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Display(Name = "Caja")]
        public Guid CashRegisterId { get; set; }
        public CashRegister CashRegister { get; set; }

        [Display(Name = "Cliente")]
        public Guid? ClientId { get; set; }
        public Client? Client { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Display(Name = "Usuario")]
        public Guid UserId { get; set; }
        public User User { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Display(Name = "Subtotal")]
        public decimal Subtotal { get; set; }

        [Display(Name = "Descuento")]
        public decimal DiscountAmount { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Display(Name = "Total")]
        public decimal TotalAmount { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Display(Name = "Método de Pago")]
        public string PaymentMethod { get; set; } = string.Empty;

        [Display(Name = "Estado")]
        public SaleStatus Status { get; set; } = SaleStatus.Completed;

        [Display(Name = "Observaciones")]
        public string? Observations { get; set; }

        // Navigation properties
        public ICollection<SaleDetail> SaleDetails { get; set; } = new List<SaleDetail>();
    }

    public class SaleDetail : IId
    {
        [Key]
        public Guid Id { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        public Guid SaleId { get; set; }
        public Sale Sale { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        public Guid ProductId { get; set; }
        public Product Product { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Range(1, int.MaxValue, ErrorMessage = "La cantidad debe ser mayor a 0")]
        [Display(Name = "Cantidad")]
        public int Quantity { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Display(Name = "Precio Unitario")]
        public decimal UnitPrice { get; set; }

        [Display(Name = "Descuento")]
        public decimal Discount { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Display(Name = "Total")]
        public decimal Total { get; set; }
    }

    public enum SaleStatus
    {
        Pending = 1,
        Completed = 2,
        Cancelled = 3
    }
}
