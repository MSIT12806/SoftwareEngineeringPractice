using System.Data;
using Microsoft.EntityFrameworkCore;

namespace MaskMap.Api.Domains
{
    public sealed class ReservationService
    {
        private readonly AppDbContext _db;

        public ReservationService(AppDbContext db)
        {
            _db = db;
        }

        public Task<Reservation?> GetByIdAsync(string id, CancellationToken cancellationToken)
        {
            return _db.Reservations
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    reservation => reservation.ReservationId == id,
                    cancellationToken);
        }

        public async Task<Reservation> CreateAsync(
            string userId,
            string pharmacyId,
            string productId,
            int quantity,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentException("User id is required.", nameof(userId));
            }

            if (quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            var quota = await _db.UserQuotas.SingleOrDefaultAsync(
                item => item.UserId == userId,
                cancellationToken);

            if (quota is null ||
                quota.Limit - quota.ReservedQuantity - quota.PurchasedQuantity < quantity)
            {
                throw new InvalidOperationException("QuotaExceeded");
            }

            var inventory = await _db.Inventories.SingleOrDefaultAsync(
                item => item.PharmacyId == pharmacyId && item.ProductId == productId,
                cancellationToken);

            if (inventory is null || inventory.AvailableQuantity < quantity)
            {
                throw new InvalidOperationException("InventoryInsufficient");
            }

            var now = DateTimeOffset.UtcNow;
            inventory.AvailableQuantity -= quantity;
            inventory.ReservedQuantity += quantity;
            inventory.LastUpdatedAt = now;
            quota.ReservedQuantity += quantity;

            var reservation = new Reservation
            {
                ReservationId = Guid.NewGuid().ToString(),
                UserId = userId,
                Status = "Reserved",
                PharmacyId = pharmacyId,
                ProductId = productId,
                Quantity = quantity,
                ExpiresAt = now.AddHours(24)
            };

            _db.Reservations.Add(reservation);

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return reservation;
        }
    }
}
