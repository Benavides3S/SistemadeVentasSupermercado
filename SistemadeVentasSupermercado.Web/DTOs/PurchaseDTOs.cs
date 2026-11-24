using SistemadeVentasSupermercado.Web.Data.Entities;

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

    /// <summary>Producto tal como lo ve el buscador de compras (incluye productos sin stock y de categorías desactivadas).</summary>
    public class PurchaseProductDTO
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Barcode { get; set; }
        public decimal Stock { get; set; }

        /// <summary>Último costo de compra; vacío si nunca se ha comprado.</summary>
        public decimal? Cost { get; set; }

        public UnitOfMeasure Unit { get; set; }
        public string UnitAbbreviation { get; set; } = string.Empty;

        /// <summary>true si se maneja por peso/volumen (acepta decimales).</summary>
        public bool AllowsDecimals { get; set; }
    }

    /// <summary>Una fila del historial de compras.</summary>
    public class PurchaseListItemDTO
    {
        public Guid Id { get; set; }
        public int Number { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string SupplierName { get; set; } = string.Empty;
        public string? SupplierInvoiceNumber { get; set; }
        public string UserName { get; set; } = string.Empty;
        public PurchaseStatus Status { get; set; }
        public decimal Total { get; set; }
    }

    /// <summary>Una compra completa, para la pantalla de detalle.</summary>
    public class PurchaseDetailDTO
    {
        public Guid Id { get; set; }
        public int Number { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string SupplierName { get; set; } = string.Empty;
        public string? SupplierInvoiceNumber { get; set; }
        public string? Notes { get; set; }
        public string UserName { get; set; } = string.Empty;
        public PurchaseStatus Status { get; set; }
        public decimal Total { get; set; }

        // ---------- Anulación ----------
        public DateTime? CancelledAtUtc { get; set; }
        public string? CancelReason { get; set; }
        public string? CancelledByName { get; set; }

        /// <summary>
        /// Se puede intentar anular mientras siga completada. Que el stock alcance se revisa al anular:
        /// si parte ya se vendió, el sistema lo rechaza y lo explica.
        /// </summary>
        public bool CanBeCancelled { get; set; }

        public List<PurchaseDetailLineDTO> Items { get; set; } = new();
    }

    public class PurchaseDetailLineDTO
    {
        public string ProductName { get; set; } = string.Empty;
        public UnitOfMeasure Unit { get; set; }
        public decimal UnitCost { get; set; }
        public decimal Quantity { get; set; }
        public decimal Total { get; set; }
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
