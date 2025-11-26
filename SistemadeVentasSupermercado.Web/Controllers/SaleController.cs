using AspNetCoreHero.ToastNotification.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Attributes;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;
using SistemadeVentasSupermercado.Web.Services.Implementations;

namespace SistemadeVentasSupermercado.Web.Controllers
{
    [Authorize]
    public class SaleController : Controller
    {
        private readonly ISaleService _saleService;
        private readonly ICashRegisterService _cashRegisterService;
        private readonly IClientService _clientService;
        private readonly INotyfService _notyfService;
        private readonly DataContext _context;
        private readonly IUsersService _usersService;

        public SaleController(
            ISaleService saleService,
            ICashRegisterService cashRegisterService,
            IClientService clientService,
            IUsersService usersService,
            INotyfService notyfService,
            DataContext context)
        {
            _saleService = saleService;
            _cashRegisterService = cashRegisterService;
            _clientService = clientService;
            _usersService = usersService;
            _notyfService = notyfService;
            _context = context; // Inicializar
        }

        [HttpGet]
        [CustomAuthorize(permission: "showSales", module: "Ventas")]
        public async Task<IActionResult> Index([FromQuery] PaginationRequest request)
        {
            Console.WriteLine("=== INICIANDO SALE/INDEX ===");

            try
            {
                Console.WriteLine("Llamando a GetPaginatedListAsync...");
                Response<PaginationResponse<SaleDTO>> response = await _saleService.GetPaginatedListAsync(request);

                Console.WriteLine($"Respuesta del servicio: IsSuccess = {response.IsSuccess}");

                if (!response.IsSuccess)
                {
                    Console.WriteLine($"Error: {response.Message}");
                    _notyfService.Error(response.Message);
                    return RedirectToAction("Index", "Home");
                }

                Console.WriteLine("Cargando vista Index...");
                return View(response.Result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"EXCEPCIÓN en Sale/Index: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                _notyfService.Error("Error al cargar las ventas");
                return RedirectToAction("Index", "Home");
            }
        }
        

        // ... (otros métodos existentes)

        [HttpGet]
        [Route("Sale/Test")]
        public async Task<IActionResult> Test()
        {
            try
            {
                Console.WriteLine("=== INICIANDO SALE/TEST ===");

                var results = new List<string>();

                // 1. Probar conexión a base de datos
                try
                {
                    var canConnect = await _context.Database.CanConnectAsync();
                    results.Add($"✅ Base de datos: {(canConnect ? "CONECTADA" : "NO CONECTADA")}");
                }
                catch (Exception ex)
                {
                    results.Add($"❌ Base de datos: ERROR - {ex.Message}");
                }

                // 2. Probar servicio de ventas
                try
                {
                    var request = new PaginationRequest();
                    var response = await _saleService.GetPaginatedListAsync(request);
                    results.Add($"✅ Servicio de ventas: {(response.IsSuccess ? "FUNCIONA" : $"ERROR: {response.Message}")}");
                }
                catch (Exception ex)
                {
                    results.Add($"❌ Servicio de ventas: ERROR - {ex.Message}");
                }

                // 3. Probar si hay ventas en la base de datos
                try
                {
                    var salesCount = await _context.Sales.CountAsync();
                    results.Add($"✅ Ventas en BD: {salesCount} registros");
                }
                catch (Exception ex)
                {
                    results.Add($"❌ Ventas en BD: ERROR - {ex.Message}");
                }

                return Content(string.Join("<br/>", results));
            }
            catch (Exception ex)
            {
                return Content($"❌ ERROR GENERAL: {ex.Message}<br/>STACK TRACE: {ex.StackTrace}");
            }
        }

        [HttpGet]
        [Route("Sale/SetupTestData")]
        public async Task<IActionResult> SetupTestData()
        {
            try
            {
                // Verificar si ya hay datos
                var existingSales = await _context.Sales.CountAsync();
                if (existingSales > 0)
                {
                    return Content($"Ya existen {existingSales} ventas en la base de datos");
                }

                // Obtener usuario actual
                var currentUser = await _usersService.GetCurrentUserAsync();
                if (!currentUser.IsSuccess)
                {
                    return Content("❌ No se pudo obtener el usuario actual");
                }

                // Crear una caja de prueba
                var cashRegister = new Data.Entities.CashRegister
                {
                    Id = Guid.NewGuid(),
                    UserId = Guid.Parse(currentUser.Result.Id),
                    OpenDate = DateTime.Now.AddHours(-2),
                    InitialAmount = 1000.00m,
                    Status = Data.Entities.CashRegisterStatus.Open
                };

                await _context.CashRegisters.AddAsync(cashRegister);
                await _context.SaveChangesAsync();

                // Crear ventas de prueba
                var sale1 = new Data.Entities.Sale
                {
                    Id = Guid.NewGuid(),
                    SaleNumber = "20240115-001",
                    SaleDate = DateTime.Now.AddHours(-1),
                    CashRegisterId = cashRegister.Id,
                    UserId = Guid.Parse(currentUser.Result.Id),
                    Subtotal = 150.00m,
                    DiscountAmount = 15.00m,
                    TotalAmount = 135.00m,
                    PaymentMethod = "Efectivo",
                    Status = Data.Entities.SaleStatus.Completed
                };

                var sale2 = new Data.Entities.Sale
                {
                    Id = Guid.NewGuid(),
                    SaleNumber = "20240115-002",
                    SaleDate = DateTime.Now,
                    CashRegisterId = cashRegister.Id,
                    UserId = Guid.Parse(currentUser.Result.Id),
                    Subtotal = 200.00m,
                    DiscountAmount = 0.00m,
                    TotalAmount = 200.00m,
                    PaymentMethod = "Tarjeta",
                    Status = Data.Entities.SaleStatus.Completed
                };

                await _context.Sales.AddRangeAsync(sale1, sale2);
                await _context.SaveChangesAsync();

                return Content("✅ Datos de prueba creados exitosamente: 1 caja abierta y 2 ventas");
            }
            catch (Exception ex)
            {
                return Content($"❌ Error creando datos de prueba: {ex.Message}<br/>{ex.StackTrace}");
            }
        }

        [HttpGet]
        [Route("Sale/CheckDatabase")]
        public async Task<IActionResult> CheckDatabase()
        {
            try
            {
                // Verificar si las tablas existen
                var salesCount = await _context.Sales.CountAsync();
                var productsCount = await _context.Products.CountAsync();
                var cashRegistersCount = await _context.CashRegisters.CountAsync();
                var saleDetailsCount = await _context.SaleDetails.CountAsync();

                return Content($"Tablas verificadas:<br/>" +
                             $"Sales = {salesCount}<br/>" +
                             $"Products = {productsCount}<br/>" +
                             $"CashRegisters = {cashRegistersCount}<br/>" +
                             $"SaleDetails = {saleDetailsCount}");
            }
            catch (Exception ex)
            {
                return Content($"ERROR DE BASE DE DATOS: {ex.Message}");
            }
        }

        [HttpGet]
        [CustomAuthorize("createSales", "Ventas")]
        public async Task<IActionResult> Create()
        {
            // Verificar si hay caja abierta
            var hasOpenCashRegister = await _cashRegisterService.HasOpenCashRegisterAsync();
            if (!hasOpenCashRegister.IsSuccess || !hasOpenCashRegister.Result)
            {
                _notyfService.Warning("Debe tener una caja abierta para realizar ventas");
                return RedirectToAction("Open", "CashRegister");
            }

            // Cargar datos necesarios para la vista
            await LoadViewDataForCreate();
            return View();
        }

        [HttpPost]
        [CustomAuthorize("createSales", "Ventas")]
        public async Task<IActionResult> Create([FromForm] CreateSaleDTO dto)
        {
            if (!ModelState.IsValid)
            {
                _notyfService.Error("Debe ajustar los errores de validación");
                await LoadViewDataForCreate();
                return View(dto);
            }

            // Validar que haya caja abierta
            var hasOpenCashRegister = await _cashRegisterService.HasOpenCashRegisterAsync();
            if (!hasOpenCashRegister.IsSuccess || !hasOpenCashRegister.Result)
            {
                _notyfService.Error("No hay caja abierta. No se puede procesar la venta");
                await LoadViewDataForCreate();
                return View(dto);
            }

            Response<SaleDTO> response = await _saleService.CreateSaleAsync(dto);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                await LoadViewDataForCreate();
                return View(dto);
            }

            _notyfService.Success($"Venta #{response.Result.SaleNumber} registrada exitosamente");
            return RedirectToAction(nameof(Details), new { id = response.Result.Id });
        }

        [HttpGet]
        [CustomAuthorize("showSales", "Ventas")]
        public async Task<IActionResult> Details(Guid id)
        {
            Response<SaleDTO> response = await _saleService.GetOneAsync(id);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction(nameof(Index));
            }

            return View(response.Result);
        }

        [HttpPost]
        [CustomAuthorize("cancelSales", "Ventas")]
        public async Task<IActionResult> Cancel([FromForm] CancelSaleDTO dto)
        {
            if (!ModelState.IsValid)
            {
                _notyfService.Error("Debe ingresar el motivo de anulación");
                return RedirectToAction(nameof(Index));
            }

            Response<SaleDTO> response = await _saleService.CancelSaleAsync(dto);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
            }
            else
            {
                _notyfService.Success($"Venta #{response.Result.SaleNumber} anulada exitosamente");
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        [CustomAuthorize("showSales", "Ventas")]
        public async Task<IActionResult> DailyReport(DateTime? date)
        {
            var targetDate = date ?? DateTime.Today;
            Response<List<SaleDTO>> response = await _saleService.GetSalesByDateRangeAsync(
                targetDate.Date, targetDate.Date.AddDays(1).AddTicks(-1));

            ViewBag.SelectedDate = targetDate;

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return View(new List<SaleDTO>());
            }

            return View(response.Result);
        }

        [HttpGet]
        public async Task<IActionResult> GetProductByCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return Json(new { success = false, message = "Código inválido" });
            }

            var response = await _saleService.GetProductByCodeAsync(code);

            if (!response.IsSuccess)
            {
                return Json(new { success = false, message = response.Message });
            }

            return Json(new
            {
                success = true,
                product = new
                {
                    id = response.Result.Id,
                    name = response.Result.Name,
                    price = response.Result.Price,
                    stock = response.Result.Stock,
                    category = response.Result.Category
                }
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetActivePaymentMethods()
        {
            var response = await _saleService.GetActivePaymentMethodsAsync();

            if (!response.IsSuccess)
            {
                return Json(new { success = false, message = response.Message });
            }

            return Json(new { success = true, paymentMethods = response.Result });
        }

        private async Task LoadViewDataForCreate()
        {
            // Cargar clientes
            var clientsResponse = await _clientService.GetListAsync();
            if (clientsResponse.IsSuccess)
            {
                ViewBag.Clients = clientsResponse.Result;
            }

            // Cargar métodos de pago activos
            var paymentMethodsResponse = await _saleService.GetActivePaymentMethodsAsync();
            if (paymentMethodsResponse.IsSuccess)
            {
                ViewBag.PaymentMethods = paymentMethodsResponse.Result;
            }

            // Cargar información de caja actual
            var currentCashRegister = await _cashRegisterService.GetCurrentCashRegisterAsync();
            if (currentCashRegister.IsSuccess)
            {
                ViewBag.CurrentCashRegister = currentCashRegister.Result;
            }
        }
    }
}