using Microsoft.EntityFrameworkCore;

namespace MaskMap.Api.Domains
{
    public class InventoryQueryHandler
    {
        private readonly AppDbContext _db;

        public InventoryQueryHandler(AppDbContext db)
        {
            _db = db;
        }

        public Task<List<Inventory>> GetInventoriesByAsync(
            string pharmacyId,
            CancellationToken cancellationToken)
        {
            return _db.Inventories
                .AsNoTracking()
                .Where(inventory => inventory.PharmacyId == pharmacyId)
                .OrderBy(inventory => inventory.ProductId)
                .ToListAsync(cancellationToken);
        }
    }
}
