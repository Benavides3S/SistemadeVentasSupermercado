namespace SistemadeVentasSupermercado.Web.DTOs
{
    public class PurchaseLineDTO
    {
        public Guid ProductId { get; set; }
        public decimal Quantity { get; set; }

        /// <summary>Costo por unidad (o por kilo, libra, litro) que pagó el negocio.</summary>
        public decimal UnitCost { get; set; }
    }

    public class CreatePurchaseDTO
    {
        public Guid SupplierId { get; set; }

        /// <summary>Número de la factura o remisión del proveedor (opcional, máx. 50).</summary>
        public string? SupplierInvoiceNumber { get; set; }

        /// <summary>Notas (opcional, máx. 200).</summary>
        public string? Notes { get; set; }

        public List<PurchaseLineDTO> Items { get; set; } = new();

        /// <summary>
        /// Código único que genera la pantalla de compras para cada registro. Si llega dos veces
        /// (reintento tras un corte de red), la compra no se registra de nuevo.
        /// </summary>
        public Guid IdempotencyKey { get; set; }
    }

    public class CancelPurchaseDTO
    {
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "El motivo es obligatorio")]
        [System.ComponentModel.DataAnnotations.StringLength(200, ErrorMessage = "El motivo puede tener máximo {1} caracteres")]
        public string Reason { get; set; } = string.Empty;
    }

    public class PurchaseResultDTO
    {
        public Guid Id { get; set; }
        public int Number { get; set; }
        public decimal Total { get; set; }

        /// <summary>Verdadero si el registro era un reintento y la compra ya estaba guardada.</summary>
        public bool AlreadyRegistered { get; set; }

        /// <summary>
        /// Aviso (no error): ya hay otra compra completada de este proveedor con el mismo número
        /// de factura. La compra se registra igual.
        /// </summary>
        public string? Warning { get; set; }
    }
}
