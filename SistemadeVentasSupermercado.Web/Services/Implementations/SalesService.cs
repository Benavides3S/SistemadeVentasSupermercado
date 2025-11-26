using AutoMapper;
using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Services.Implementations
{
    public class SaleService : CustomQueryableOperationsService, ISaleService
    {
        private readonly DataContext _context;
        private readonly IMapper _mapper;
        private readonly IUsersService _usersService;
        private readonly ICashRegisterService _cashRegisterService;
        private readonly IProductService _productService;
        private readonly ILogger<SaleService> _logger;

        public SaleService(
            DataContext context,
        IMapper mapper,
        IUsersService usersService,
        ICashRegisterService cashRegisterService,
        IProductService productService,
        ILogger<SaleService> logger) : base(context, mapper)
        {
            _context = context;
            _mapper = mapper;
            _usersService = usersService;
            _cashRegisterService = cashRegisterService;
            _productService = productService;
            _logger = logger;
        }

        public async Task<Response<SaleDTO>> CreateSaleAsync(CreateSaleDTO dto)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Validar que haya caja abierta
                var currentCashRegister = await _cashRegisterService.GetCurrentCashRegisterAsync();
                if (!currentCashRegister.IsSuccess)
                {
                    _logger.LogWarning("Intento de venta sin caja abierta");
                    return Response<SaleDTO>.Failure("Debe tener una caja abierta para realizar ventas");
                }

                // Validar detalles de venta
                if (!dto.SaleDetails.Any())
                {
                    _logger.LogWarning("Intento de venta sin productos");
                    return Response<SaleDTO>.Failure("La venta debe tener al menos un producto");
                }

                // Obtener usuario actual
                var currentUser = await _usersService.GetCurrentUserAsync();
                if (!currentUser.IsSuccess)
                {
                    _logger.LogWarning("Usuario no autenticado intentando realizar venta");
                    return Response<SaleDTO>.Failure("Usuario no autenticado");
                }

                // Validar stock y calcular totales
                decimal subtotal = 0;
                foreach (var detail in dto.SaleDetails)
                {
                    var productResponse = await _productService.GetOneAsync(detail.ProductId);
                    if (!productResponse.IsSuccess)
                    {
                        _logger.LogWarning("Producto no encontrado: {ProductId}", detail.ProductId);
                        return Response<SaleDTO>.Failure($"Producto no encontrado: {detail.ProductId}");
                    }

                    var product = productResponse.Result;
                    if (product.Stock < detail.Quantity)
                    {
                        _logger.LogWarning("Stock insuficiente: {ProductName}, Stock: {Stock}, Solicitado: {Quantity}",
                            product.Name, product.Stock, detail.Quantity);
                        return Response<SaleDTO>.Failure($"Stock insuficiente para: {product.Name}. Stock disponible: {product.Stock}");
                    }

                    detail.UnitPrice = product.Price;
                    detail.Total = (detail.UnitPrice * detail.Quantity) - detail.Discount;
                    subtotal += detail.Total;
                }

                // Aplicar descuento general
                decimal discountAmount = 0;
                if (dto.GeneralDiscountPercent > 0)
                {
                    discountAmount = subtotal * (dto.GeneralDiscountPercent / 100);
                }

                decimal totalAmount = subtotal - discountAmount;

                // Generar número de venta
                var saleNumber = await GenerateSaleNumberAsync();

                // Crear venta
                var sale = new Sale
                {
                    Id = Guid.NewGuid(),
                    SaleNumber = saleNumber,
                    SaleDate = DateTime.Now,
                    CashRegisterId = currentCashRegister.Result.Id,
                    ClientId = dto.ClientId,
                    UserId = Guid.Parse(currentUser.Result.Id),
                    Subtotal = subtotal,
                    DiscountAmount = discountAmount,
                    TotalAmount = totalAmount,
                    PaymentMethod = dto.PaymentMethod,
                    Status = SaleStatus.Completed,
                    Observations = dto.Observations
                };

                // Crear detalles de venta y actualizar stock
                foreach (var detailDto in dto.SaleDetails)
                {
                    var saleDetail = new SaleDetail
                    {
                        Id = Guid.NewGuid(),
                        SaleId = sale.Id,
                        ProductId = detailDto.ProductId,
                        Quantity = detailDto.Quantity,
                        UnitPrice = detailDto.UnitPrice,
                        Discount = detailDto.Discount,
                        Total = detailDto.Total
                    };

                    sale.SaleDetails.Add(saleDetail);

                    // Actualizar stock del producto
                    var product = await _context.Products.FindAsync(detailDto.ProductId);
                    if (product != null)
                    {
                        product.Stock -= detailDto.Quantity;
                        _context.Products.Update(product);
                        _logger.LogInformation("Stock actualizado: {ProductName}, Nuevo stock: {Stock}",
                            product.Name, product.Stock);
                    }
                }

                await _context.Sales.AddAsync(sale);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("Venta creada exitosamente: {SaleNumber}, Total: {Total}",
                    saleNumber, totalAmount);

                var saleDto = _mapper.Map<SaleDTO>(sale);
                return Response<SaleDTO>.Success(saleDto, "Venta registrada exitosamente");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear la venta");
                return Response<SaleDTO>.Failure($"Error al crear la venta: {ex.Message}");
            }
        }
        public async Task<Response<SaleDTO>> CancelSaleAsync(CancelSaleDTO dto)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var sale = await _context.Sales
                    .Include(s => s.SaleDetails)
                    .FirstOrDefaultAsync(s => s.Id == dto.SaleId);

                if (sale == null)
                {
                    _logger.LogWarning("Intento de anular venta no encontrada: {SaleId}", dto.SaleId);
                    return Response<SaleDTO>.Failure("Venta no encontrada");
                }

                if (sale.Status == SaleStatus.Cancelled)
                {
                    _logger.LogWarning("Intento de anular venta ya anulada: {SaleNumber}", sale.SaleNumber);
                    return Response<SaleDTO>.Failure("La venta ya está anulada");
                }

                // Revertir stock
                foreach (var detail in sale.SaleDetails)
                {
                    var product = await _context.Products.FindAsync(detail.ProductId);
                    if (product != null)
                    {
                        product.Stock += detail.Quantity;
                        _context.Products.Update(product);
                        _logger.LogInformation("Stock revertido: {ProductName}, Nuevo stock: {Stock}",
                            product.Name, product.Stock);
                    }
                }

                // Anular venta
                sale.Status = SaleStatus.Cancelled;
                sale.Observations += $"\nANULADA: {dto.CancellationReason} - {DateTime.Now}";

                _context.Sales.Update(sale);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("Venta anulada: {SaleNumber}, Motivo: {Reason}",
                    sale.SaleNumber, dto.CancellationReason);

                var saleDto = _mapper.Map<SaleDTO>(sale);
                return Response<SaleDTO>.Success(saleDto, "Venta anulada exitosamente");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al anular la venta: {SaleId}", dto.SaleId);
                return Response<SaleDTO>.Failure($"Error al anular la venta: {ex.Message}");
            }
        }
        private async Task<string> GenerateSaleNumberAsync()
        {
            try
            {
                var today = DateTime.Today;
                var lastSale = await _context.Sales
                    .Where(s => s.SaleDate >= today && s.SaleDate < today.AddDays(1))
                    .OrderByDescending(s => s.SaleNumber)
                    .FirstOrDefaultAsync();

                if (lastSale == null)
                {
                    return $"{today:yyyyMMdd}-001";
                }

                var lastNumber = int.Parse(lastSale.SaleNumber.Split('-')[1]);
                return $"{today:yyyyMMdd}-{(lastNumber + 1):D3}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generando número de venta");
                // Fallback: usar timestamp
                return $"{DateTime.Now:yyyyMMddHHmmss}";
            }
        }

        public async Task<Response<SaleDTO>> GetOneAsync(Guid id)
        {
            try
            {
                var sale = await _context.Sales
                    .Include(s => s.CashRegister)
                    .Include(s => s.Client)
                    .Include(s => s.User)
                    .Include(s => s.SaleDetails)
                        .ThenInclude(sd => sd.Product)
                    .FirstOrDefaultAsync(s => s.Id == id);

                if (sale == null)
                    return Response<SaleDTO>.Failure("Venta no encontrada");

                var saleDto = _mapper.Map<SaleDTO>(sale);
                return Response<SaleDTO>.Success(saleDto);
            }
            catch (Exception ex)
            {
                return Response<SaleDTO>.Failure(ex);
            }
        }

        public async Task<Response<List<SaleDTO>>> GetListAsync()
        {
            try
            {
                var sales = await _context.Sales
                    .Include(s => s.CashRegister)
                    .Include(s => s.Client)
                    .Include(s => s.User)
                    .Include(s => s.SaleDetails)
                    .OrderByDescending(s => s.SaleDate)
                    .ToListAsync();

                var saleDtos = _mapper.Map<List<SaleDTO>>(sales);
                return Response<List<SaleDTO>>.Success(saleDtos);
            }
            catch (Exception ex)
            {
                return Response<List<SaleDTO>>.Failure(ex);
            }
        }

        public async Task<Response<PaginationResponse<SaleDTO>>> GetPaginatedListAsync(PaginationRequest request)
        {
            IQueryable<Sale> query = _context.Sales
                .Include(s => s.CashRegister)
                .Include(s => s.Client)
                .Include(s => s.User)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(request.Filter))
            {
                query = query.Where(s => s.SaleNumber.Contains(request.Filter) ||
                                         s.Client.Name.Contains(request.Filter) ||
                                         s.PaymentMethod.Contains(request.Filter));
            }

            query = query.OrderByDescending(s => s.SaleDate);

            return await GetPaginationAsync<Sale, SaleDTO>(request, query);
        }

        public async Task<Response<List<SaleDTO>>> GetSalesByDateRangeAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                var sales = await _context.Sales
                    .Include(s => s.Client)
                    .Include(s => s.User)
                    .Include(s => s.SaleDetails)
                    .Where(s => s.SaleDate >= startDate && s.SaleDate <= endDate && s.Status == SaleStatus.Completed)
                    .OrderByDescending(s => s.SaleDate)
                    .ToListAsync();

                var saleDtos = _mapper.Map<List<SaleDTO>>(sales);
                return Response<List<SaleDTO>>.Success(saleDtos);
            }
            catch (Exception ex)
            {
                return Response<List<SaleDTO>>.Failure(ex);
            }
        }

        public async Task<Response<List<SaleDTO>>> GetSalesByCashRegisterAsync(Guid cashRegisterId)
        {
            try
            {
                var sales = await _context.Sales
                    .Include(s => s.Client)
                    .Include(s => s.SaleDetails)
                    .Where(s => s.CashRegisterId == cashRegisterId && s.Status == SaleStatus.Completed)
                    .OrderBy(s => s.SaleDate)
                    .ToListAsync();

                var saleDtos = _mapper.Map<List<SaleDTO>>(sales);
                return Response<List<SaleDTO>>.Success(saleDtos);
            }
            catch (Exception ex)
            {
                return Response<List<SaleDTO>>.Failure(ex);
            }
        }

        public async Task<Response<decimal>> GetDailySalesTotalAsync(DateTime date)
        {
            try
            {
                var startDate = date.Date;
                var endDate = date.Date.AddDays(1).AddTicks(-1);

                var total = await _context.Sales
                    .Where(s => s.SaleDate >= startDate && s.SaleDate <= endDate && s.Status == SaleStatus.Completed)
                    .SumAsync(s => s.TotalAmount);

                return Response<decimal>.Success(total);
            }
            catch (Exception ex)
            {
                return Response<decimal>.Failure(ex);
            }
        }

        public async Task<Response<ProductDTO>> GetProductByCodeAsync(string code)
        {
            try
            {
                // Asumiendo que el código podría ser el ID, nombre o un código de barras
                var product = await _context.Products
                    .FirstOrDefaultAsync(p => p.Id.ToString() == code ||
                                             p.Name.Contains(code) ||
                                             p.Name.ToLower() == code.ToLower());

                if (product == null)
                    return Response<ProductDTO>.Failure("Producto no encontrado");

                var productDto = _mapper.Map<ProductDTO>(product);
                return Response<ProductDTO>.Success(productDto);
            }
            catch (Exception ex)
            {
                return Response<ProductDTO>.Failure(ex);
            }
        }

        public async Task<Response<List<PaymentMethodDTO>>> GetActivePaymentMethodsAsync()
        {
            try
            {
                var paymentMethods = await _context.PaymentMethods
                    .Where(pm => pm.IsActive)
                    .OrderBy(pm => pm.Name)
                    .ToListAsync();

                var paymentMethodDtos = _mapper.Map<List<PaymentMethodDTO>>(paymentMethods);
                return Response<List<PaymentMethodDTO>>.Success(paymentMethodDtos);
            }
            catch (Exception ex)
            {
                return Response<List<PaymentMethodDTO>>.Failure(ex);
            }
        }
    }
}
