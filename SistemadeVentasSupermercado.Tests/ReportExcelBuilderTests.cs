using ClosedXML.Excel;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Helpers;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>Abre el Excel generado y revisa su contenido (sin base de datos).</summary>
    public class ReportExcelBuilderTests
    {
        [Fact]
        public void El_excel_tiene_dos_hojas_con_numeros_reales()
        {
            DailySalesReportDTO daily = new DailySalesReportDTO
            {
                From = new DateOnly(2020, 2, 10),
                To = new DateOnly(2020, 2, 11),
                Days =
                {
                    new DailySalesRowDTO { Date = new DateOnly(2020, 2, 10), SalesCount = 3, SalesTotal = 60000m, CashTotal = 20000m, CardTotal = 10000m, TransferTotal = 30000m },
                    new DailySalesRowDTO { Date = new DateOnly(2020, 2, 11), ReturnsCount = 1, ReturnsTotal = 10000m }
                },
                Totals = new DailySalesRowDTO { SalesCount = 3, SalesTotal = 60000m, CashTotal = 20000m, CardTotal = 10000m, TransferTotal = 30000m, ReturnsCount = 1, ReturnsTotal = 10000m }
            };
            List<TopProductDTO> top = new()
            {
                new TopProductDTO { ProductName = "Carne", Unit = UnitOfMeasure.Kilogram, QuantitySold = 2.5m, QuantityReturned = 1.5m, SalesTotal = 75000m, ReturnedTotal = 45000m }
            };

            byte[] archivo = ReportExcelBuilder.Build("Mi Súper", daily, top);

            using XLWorkbook libro = new XLWorkbook(new MemoryStream(archivo));
            Assert.Equal(new[] { "Ventas por día", "Más vendidos" }, libro.Worksheets.Select(h => h.Name));

            IXLWorksheet dias = libro.Worksheet("Ventas por día");
            Assert.Equal("Mi Súper", dias.Cell(1, 1).GetString());
            Assert.Equal(new DateTime(2020, 2, 10), dias.Cell(5, 1).GetDateTime());
            Assert.Equal(60000m, dias.Cell(5, 6).GetValue<decimal>());      // número, no texto
            Assert.Equal(-10000m, dias.Cell(6, 9).GetValue<decimal>());     // neto del día 11
            Assert.Equal("TOTAL", dias.Cell(7, 1).GetString());
            Assert.Equal(50000m, dias.Cell(7, 9).GetValue<decimal>());

            IXLWorksheet productos = libro.Worksheet("Más vendidos");
            Assert.Equal("Carne", productos.Cell(5, 2).GetString());
            Assert.Equal("kg", productos.Cell(5, 3).GetString());
            Assert.Equal(1m, productos.Cell(5, 6).GetValue<decimal>());
            Assert.Equal(30000m, productos.Cell(5, 9).GetValue<decimal>());
        }
    }
}
