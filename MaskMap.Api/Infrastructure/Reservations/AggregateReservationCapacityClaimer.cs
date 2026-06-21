using MaskMap.Api.Domains;
using Microsoft.EntityFrameworkCore;

namespace MaskMap.Api.Infrastructure.Reservations;

public sealed class AggregateReservationCapacityClaimer
    : IReservationCapacityClaimer
{
    private readonly AppDbContext _db;

    public AggregateReservationCapacityClaimer(AppDbContext db)
    {
        _db = db;
    }

    public async Task<ReservationCapacityClaimResult> TryClaimAsync(
        string userId,
        string pharmacyId,
        string productId,
        int quantity,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var quota = await _db.UserQuotas.SingleOrDefaultAsync(
            item => item.UserId == userId,
            cancellationToken);

        if (quota is null || !quota.CanReserve(quantity))
        {
            return ReservationCapacityClaimResult.QuotaExceeded;
        }

        var inventory = await _db.Inventories.SingleOrDefaultAsync(
            item => item.PharmacyId == pharmacyId &&
                    item.ProductId == productId,
            cancellationToken);

        if (inventory is null || !inventory.CanReserve(quantity))
        {
            return ReservationCapacityClaimResult.InventoryInsufficient;
        }

        quota.Reserve(quantity);
        inventory.Reserve(quantity, occurredAt);

        // Let DbUpdateConcurrencyException escape. The application service must roll
        // back and retry the complete transaction, not retry inside this transaction.
        await _db.SaveChangesAsync(cancellationToken);
        return ReservationCapacityClaimResult.Succeeded;
    }
}
