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

        public Task<List<Reservation>> GetForUserAsync(
            string userId,
            CancellationToken cancellationToken)
        {
            return _db.Reservations
                .AsNoTracking()
                .Where(reservation => reservation.UserId == userId)
                .OrderByDescending(reservation => reservation.ExpiresAt)
                .ThenBy(reservation => reservation.ReservationId)
                .ToListAsync(cancellationToken);
        }

        public async Task<Reservation> CreateAsync(
            string userId,
            string idempotencyKey,
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

            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                throw new ArgumentException("Idempotency key is required.", nameof(idempotencyKey));
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            var existingOperation = await _db.ReservationOperations
                .SingleOrDefaultAsync(
                    operation => operation.UserId == userId &&
                                 operation.IdempotencyKey == idempotencyKey,
                    cancellationToken);

            if (existingOperation is not null)
            {
                if (existingOperation.PharmacyId != pharmacyId ||
                    existingOperation.ProductId != productId ||
                    existingOperation.Quantity != quantity)
                {
                    throw new ReservationConflictException(
                        "IdempotencyKeyConflict",
                        "The idempotency key was already used with a different request body.");
                }

                return await _db.Reservations.SingleAsync(
                    reservation => reservation.ReservationId == existingOperation.ReservationId,
                    cancellationToken);
            }

            var quota = await _db.UserQuotas.SingleOrDefaultAsync(
                item => item.UserId == userId,
                cancellationToken);

            if (quota is null ||
                quota.Limit - quota.ReservedQuantity - quota.PurchasedQuantity < quantity)
            {
                throw new ReservationConflictException(
                    "QuotaExceeded",
                    "The user does not have enough quota for this reservation.");
            }

            // Current implementation reads a tracked Inventory row and updates it later in
            // SaveChanges. Under Serializable, concurrent requests can all retain shared
            // locks on the same hot row and then deadlock while converting them to exclusive
            // locks. Two alternative fixes are maintained for comparison:
            // - CreateReservationSolusion/UseCas uses one conditional atomic UPDATE.
            // - CreateReservationSolusion/UseUpdlock takes UPDLOCK while reading the row.
            var inventory = await _db.Inventories.SingleOrDefaultAsync(
                item => item.PharmacyId == pharmacyId && item.ProductId == productId,
                cancellationToken);

            if (inventory is null || inventory.AvailableQuantity < quantity)
            {
                throw new ReservationConflictException(
                    "InventoryInsufficient",
                    "The pharmacy does not have enough inventory for this reservation.");
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
            _db.ReservationOperations.Add(new ReservationOperation
            {
                UserId = userId,
                IdempotencyKey = idempotencyKey,
                PharmacyId = pharmacyId,
                ProductId = productId,
                Quantity = quantity,
                ReservationId = reservation.ReservationId,
                CreatedAt = now
            });

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return reservation;
        }
    }
}
