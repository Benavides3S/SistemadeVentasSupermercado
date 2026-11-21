using ClosedXML.Excel;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Web.Helpers
{
    /// <summary>
    /// Arma el archivo Excel de reportes con ClosedXML (licencia MIT).
    /// Los números se guardan como números (no como texto), así se pueden sumar en Excel;
    /// el formato de miles y decimales lo pone Excel según la configuración del equipo.
    /// </summary>
    public static class ReportExcelBuilder
    {
        private const string MoneyFormat = "$ #,##0.##";
        private const string QuantityFormat = "#,##0.###";

        public static byte[] Build(string businessName, DailySalesReportDTO daily, List<TopProductDTO> top)
        {
            using XLWorkbook workbook = new XLWorkbook();

            BuildDailySheet(workbook, businessName, daily);
            BuildTopSheet(workbook, businessName, daily.From, daily.To, top);

            using MemoryStream stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private static void Title(IXLWorksheet sheet, string businessName, string title, DateOnly from, DateOnly to, int lastColumn)
        {
            sheet.Cell(1, 1).Value = businessName;
            sheet.Cell(1, 1).Style.Font.Bold = true;
            sheet.Cell(1, 1).Style.Font.FontSize = 14;
            sheet.Cell(2, 1).Value = $"{title} del {from:dd/MM/yyyy} al {to:dd/MM/yyyy}";
            sheet.Range(1, 1, 1, lastColumn).Merge();
            sheet.Range(2, 1, 2, lastColumn).Merge();
        }

        private static void Header(IXLWorksheet sheet, int row, params string[] titles)
        {
            for (int i = 0; i < titles.Length; i++)
                sheet.Cell(row, i + 1).Value = titles[i];

            IXLRange header = sheet.Range(row, 1, row, titles.Length);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        private static void BuildDailySheet(XLWorkbook workbook, string businessName, DailySalesReportDTO daily)
        {
            IXLWorksheet sheet = workbook.Worksheets.Add("Ventas por día");
            Title(sheet, businessName, "Ventas por día", daily.From, daily.To, 9);
            Header(sheet, 4, "Fecha", "Ventas", "Efectivo", "Tarjeta", "Transferencia", "Total vendido", "Devoluciones", "Total devuelto", "Neto");

            int row = 5;
            foreach (DailySalesRowDTO day in daily.Days)
            {
                WriteDailyRow(sheet, row, day);
                sheet.Cell(row, 1).Value = day.Date.ToDateTime(TimeOnly.MinValue);
                sheet.Cell(row, 1).Style.DateFormat.Format = "dd/mm/yyyy";
                row++;
            }

            WriteDailyRow(sheet, row, daily.Totals);
            sheet.Cell(row, 1).Value = "TOTAL";
            sheet.Row(row).Style.Font.Bold = true;

            sheet.Columns(3, 6).Style.NumberFormat.Format = MoneyFormat;
            sheet.Columns(8, 9).Style.NumberFormat.Format = MoneyFormat;
            sheet.SheetView.FreezeRows(4);
            sheet.Columns().AdjustToContents();
        }

        private static void WriteDailyRow(IXLWorksheet sheet, int row, DailySalesRowDTO day)
        {
            sheet.Cell(row, 2).Value = day.SalesCount;
            sheet.Cell(row, 3).Value = day.CashTotal;
            sheet.Cell(row, 4).Value = day.CardTotal;
            sheet.Cell(row, 5).Value = day.TransferTotal;
            sheet.Cell(row, 6).Value = day.SalesTotal;
            sheet.Cell(row, 7).Value = day.ReturnsCount;
            sheet.Cell(row, 8).Value = day.ReturnsTotal;
            sheet.Cell(row, 9).Value = day.NetTotal;
        }

        private static void BuildTopSheet(XLWorkbook workbook, string businessName, DateOnly from, DateOnly to, List<TopProductDTO> top)
        {
            IXLWorksheet sheet = workbook.Worksheets.Add("Más vendidos");
            Title(sheet, businessName, "Productos más vendidos", from, to, 9);
            Header(sheet, 4, "#", "Producto", "Unidad", "Vendido", "Devuelto", "Neto", "Total vendido", "Total devuelto", "Neto $");

            int row = 5;
            int position = 1;
            foreach (TopProductDTO product in top)
            {
                sheet.Cell(row, 1).Value = position++;
                sheet.Cell(row, 2).Value = product.ProductName;
                sheet.Cell(row, 3).Value = product.Unit.Abbreviation();
                sheet.Cell(row, 4).Value = product.QuantitySold;
                sheet.Cell(row, 5).Value = product.QuantityReturned;
                sheet.Cell(row, 6).Value = product.NetQuantity;
                sheet.Cell(row, 7).Value = product.SalesTotal;
                sheet.Cell(row, 8).Value = product.ReturnedTotal;
                sheet.Cell(row, 9).Value = product.NetTotal;
                row++;
            }

            sheet.Columns(4, 6).Style.NumberFormat.Format = QuantityFormat;
            sheet.Columns(7, 9).Style.NumberFormat.Format = MoneyFormat;
            sheet.SheetView.FreezeRows(4);
            sheet.Columns().AdjustToContents();
        }
    }
}
