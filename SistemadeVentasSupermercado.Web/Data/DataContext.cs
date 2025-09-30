using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.NewFolder.Entities;
namespace SistemadeVentasSupermercado.Web.Data
{
    public class DataContext : DbContext
    {
        public DataContext( DbContextOptions<DataContext> options) : base(options) { 
        
        }
        public DbSet<Section> Sections { get; set; }
        public DbSet<Product> Products { get; set; }
    }
}
