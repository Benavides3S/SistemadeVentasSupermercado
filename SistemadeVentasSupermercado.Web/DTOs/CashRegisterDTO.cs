using SistemadeVentasSupermercado.Web.Data.Entities;
using System.ComponentModel.DataAnnotations;

namespace SistemadeVentasSupermercado.Web.DTOs
{
    public class CashRegisterDTO
    {
        public Guid Id { get; set; }

        [Display(Name = "Usuario")]
        public Guid UserId { get; set; }

        [Display(Name = "Nombre Usuario")]
        public string UserName { get; set; } = string.Empty;

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Display(Name = "Fecha Apertura")]
        public DateTime OpenDate { get; set; } = DateTime.Now;

        [Display(Name = "Fecha Cierre")]
        public DateTime? CloseDate { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Range(0, double.MaxValue, ErrorMessage = "El monto inicial debe ser mayor o igual a 0")]
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
        public CashRegisterStatus Status { get; set; }

        [Display(Name = "Observaciones")]
        public string? Observations { get; set; }
    }

    public class OpenCashRegisterDTO
    {
        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Range(0, double.MaxValue, ErrorMessage = "El monto inicial debe ser mayor o igual a 0")]
        [Display(Name = "Monto Inicial")]
        public decimal InitialAmount { get; set; }

        [Display(Name = "Observaciones")]
        public string? Observations { get; set; }
    }

    public class CloseCashRegisterDTO
    {
        public Guid CashRegisterId { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Range(0, double.MaxValue, ErrorMessage = "El monto final debe ser mayor o igual a 0")]
        [Display(Name = "Monto Final en Efectivo")]
        public decimal FinalCashAmount { get; set; }

        [Display(Name = "Observaciones de Cierre")]
        public string? Observations { get; set; }
    }
}
