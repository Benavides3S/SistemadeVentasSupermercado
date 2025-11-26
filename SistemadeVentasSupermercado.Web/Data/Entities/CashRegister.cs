using SistemadeVentasSupermercado.Web.Data.Abstractions;
using System.ComponentModel.DataAnnotations;

namespace SistemadeVentasSupermercado.Web.Data.Entities
{
    public class CashRegister : IId
    {
        [Key]
        public Guid Id { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Display(Name = "Usuario")]
        public Guid UserId { get; set; }
        public User User { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Display(Name = "Fecha Apertura")]
        public DateTime OpenDate { get; set; } = DateTime.Now;

        [Display(Name = "Fecha Cierre")]
        public DateTime? CloseDate { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Display(Name = "Monto Inicial")]
        public decimal InitialAmount { get; set; }

        [Display(Name = "Monto Final")]
        public decimal? FinalAmount { get; set; }

        [Display(Name = "Total Ventas")]
        public decimal? TotalSales { get; set; }

        [Display(Name = "Total Efectivo")]
        public decimal? TotalCash { get; set; }

        [Display(Name = "Total Tarjeta")]
        public decimal? TotalCard { get; set; }

        [Display(Name = "Total Transferencias")]
        public decimal? TotalTransfer { get; set; }

        [Display(Name = "Diferencia")]
        public decimal? Difference { get; set; }

        [Display(Name = "Estado")]
        public CashRegisterStatus Status { get; set; } = CashRegisterStatus.Open;

        [Display(Name = "Observaciones")]
        public string? Observations { get; set; }

        // Navigation properties
        public ICollection<Sale> Sales { get; set; } = new List<Sale>();
    }

    public enum CashRegisterStatus
    {
        Open = 1,
        Closed = 2
    }
}
