using ClosedXML.Excel;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Web.Helpers
{
    /// <summary>
    /// Excel del reporte por cajero. Los números van como números y las fechas como fechas (hora de Colombia).
    /// Los textos que escriben las personas se guardan como texto, nunca como fórmula.
    /// </summary>
    public static class CashierReportExcelBuilder
    {
        public const string PartialNotice =
            "Reporte parcial: no incluye las cifras de quien lo descargó, porque tenía la caja abierta.";
        private const string DateFormat = "dd/mm/yyyy hh:mm";
        private const int HeaderRow = 5;

        /// <summary>rows: las filas del resumen que van en el archivo.</summary>
        public static byte[] Build(string businessName, CashierDetailsDTO details, List<CashierRowDTO> rows)
        {
            using XLWorkbook workbook = new XLWorkbook();

            Sheet(workbook, businessName, details, "Resumen",
            [
                "Persona", "Ventas", "Total vendido", "Efectivo", "Tarjeta", "Transferencia",
                "Anuladas (cobró)", "Valor anuladas (cobró)", "Anulaciones ejecutadas", "Valor ejecutadas",
                "Devoluciones registradas", "Valor registradas", "Devoluciones sobre sus ventas", "Valor sobre sus ventas",
                "Turnos cerrados", "Turnos descuadrados", "Faltantes", "Sobrantes", "Turnos abiertos"
            ],
            rows.Select(r => new XLCellValue[]
            {
                r.IsDeactivated ? $"{r.Name} (desactivado)" : r.Name,
                r.SalesCount, r.SalesTotal, r.CashTotal, r.CardTotal, r.TransferTotal,
                r.OwnSalesCancelledCount, r.OwnSalesCancelledTotal, r.CancellationsDoneCount, r.CancellationsDoneTotal,
                r.ReturnsRegisteredCount, r.ReturnsRegisteredTotal, r.ReturnsOnOwnSalesCount, r.ReturnsOnOwnSalesTotal,
                r.ClosedSessionsCount, r.UnbalancedSessionsCount, r.ShortageTotal, r.SurplusTotal, r.OpenSessionsCount
            }),
            moneyColumns: [3, 4, 5, 6, 8, 10, 12, 14, 17, 18], dateColumns: []);

            Sheet(workbook, businessName, details, "Anulaciones",
                ["Venta", "Cobrada", "Anulada", "Valor", "Cobró", "Anuló", "Autorizó", "Motivo"],
                details.Cancellations.Select(c => new XLCellValue[]
                {
                    c.SaleNumber, Local(c.SoldAtUtc), Local(c.CancelledAtUtc), c.Total, c.ChargedByName,
                    c.CancelledByName, c.AuthorizedByName ?? string.Empty, c.Reason
                }),
                moneyColumns: [4], dateColumns: [2, 3]);

            Sheet(workbook, businessName, details, "Devoluciones",
                ["Devolución", "Venta", "Fecha", "Valor", "Reembolso", "Cobró la venta", "Registró", "Autorizó", "Motivo"],
                details.Returns.Select(r => new XLCellValue[]
                {
                    r.ReturnNumber, r.SaleNumber, Local(r.CreatedAtUtc), r.Total, r.RefundMethod.Label(),
                    r.ChargedByName, r.RegisteredByName, r.AuthorizedByName ?? string.Empty, r.Reason
                }),
                moneyColumns: [4], dateColumns: [3]);

            Sheet(workbook, businessName, details, "Turnos",
            [
                "Turno", "Cajero", "Estado", "Apertura", "Cierre", "Esperado", "Contado", "Diferencia",
                "Cerrado por", "Lo cerró otra persona", "Observaciones"
            ],
            details.Sessions.Select(s => new XLCellValue[]
            {
                s.Number, s.CashierName, s.IsOpen ? "Abierto" : "Cerrado", Local(s.OpenedAtUtc),
                s.ClosedAtUtc.HasValue ? Local(s.ClosedAtUtc.Value) : Blank.Value,
                Number(s.ExpectedAmount), Number(s.CountedAmount), Number(s.Difference),
                s.ClosedByName ?? string.Empty, s.ClosedByOther ? "Sí" : string.Empty, s.Notes ?? string.Empty
            }),
            moneyColumns: [6, 7, 8], dateColumns: [4, 5]);

            using MemoryStream stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private static XLCellValue Local(DateTime utc) => DisplayFormat.ToColombiaTime(utc);

        private static XLCellValue Number(decimal? value) => value.HasValue ? value.Value : Blank.Value;

        private static void Sheet(XLWorkbook workbook, string businessName, CashierDetailsDTO details, string name,
            string[] titles, IEnumerable<XLCellValue[]> rows, int[] moneyColumns, int[] dateColumns)
        {
            IXLWorksheet sheet = workbook.Worksheets.Add(name);
            ReportExcelBuilder.Title(sheet, businessName, $"Reporte por cajero ({name})", details.From, details.To, titles.Length);
            if (details.OwnFiguresHidden)
                sheet.Cell(3, 1).Value = PartialNotice;
            ReportExcelBuilder.Header(sheet, HeaderRow - 1, titles);

            int row = HeaderRow;
            foreach (XLCellValue[] values in rows)
            {
                for (int i = 0; i < values.Length; i++)
                    sheet.Cell(row, i + 1).Value = values[i];
                row++;
            }

            foreach (int column in moneyColumns)
                sheet.Column(column).Style.NumberFormat.Format = ReportExcelBuilder.MoneyFormat;
            foreach (int column in dateColumns)
                sheet.Column(column).Style.DateFormat.Format = DateFormat;

            sheet.SheetView.FreezeRows(HeaderRow - 1);
            // El ancho se calcula sin las filas del título, que ocupan varias columnas.
            sheet.Columns(1, titles.Length).AdjustToContents(HeaderRow - 1, Math.Max(row, HeaderRow), 8, 60);
        }
    }
}
