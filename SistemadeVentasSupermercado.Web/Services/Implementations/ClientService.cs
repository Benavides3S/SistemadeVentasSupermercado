using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Mappings;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Services.Implementations
{
    public class ClientsService : CustomQueryableOperationsService, IClientService
    {
        private readonly DataContext _context;

        public ClientsService(DataContext context) : base(context)
        {
            _context = context;
        }

        public async Task<Response<ClientDTO>> CreateAsync(ClientDTO dto)
            => await CreateAsync<Client, ClientDTO>(dto, d => d.ToEntity());

        public async Task<Response<object>> DeleteAsync(Guid id)
            => await DeleteAsync<Client>(id);

        public async Task<Response<ClientDTO>> EditAsync(ClientDTO dto)
            => await EditAsync<Client, ClientDTO>(dto, dto.Id, (d, c) => d.ApplyTo(c));

        public async Task<Response<List<ClientDTO>>> GetListAsync()
            => await GetCompleteListAsync<Client, ClientDTO>(c => c.ToDTO());

        public async Task<Response<PaginationResponse<ClientDTO>>> GetPaginatedListAsync(PaginationRequest request)
        {
            IQueryable<Client> query = _context.Clients.AsQueryable();

            if (!string.IsNullOrWhiteSpace(request.Filter))
            {
                query = query.Where(c =>
                    c.Name.ToLower().Contains(request.Filter.ToLower()) ||
                    c.Email.ToLower().Contains(request.Filter.ToLower()));
            }

            return await GetPaginationAsync<Client, ClientDTO>(request, c => c.ToDTO(), query);
        }

        public async Task<Response<ClientDTO>> GetOneAsync(Guid id)
            => await GetOneAsync<Client, ClientDTO>(id, c => c.ToDTO());
    }
}
