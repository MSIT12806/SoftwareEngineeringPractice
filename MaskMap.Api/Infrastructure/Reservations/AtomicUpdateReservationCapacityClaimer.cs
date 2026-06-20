using MaskMap.Api.Domains;
using Microsoft.EntityFrameworkCore;

namespace MaskMap.Api.Infrastructure.Reservations;

public sealed class AtomicUpdateReservationCapacityClaimer
    : IReservationCapacityClaimer
{
    private readonly AppDbContext _db;

    public AtomicUpdateReservationCapacityClaimer(AppDbContext db)
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
        var occupiedQuotaRows = await _db.UserQuotas
            .Where(item => item.UserId == userId &&
                           item.Limit - item.ReservedQuantity -
                           item.PurchasedQuantity >= quantity)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    item => item.ReservedQuantity,
                    item => item.ReservedQuantity + quantity),
                cancellationToken);

        if (occupiedQuotaRows == 0)
        {
            return ReservationCapacityClaimResult.QuotaExceeded;
        }

        var occupiedInventoryRows = await _db.Inventories
            .Where(item => item.PharmacyId == pharmacyId &&
                           item.ProductId == productId &&
                           item.AvailableQuantity >= quantity)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        item => item.AvailableQuantity,
                        item => item.AvailableQuantity - quantity)
                    .SetProperty(
                        item => item.ReservedQuantity,
                        item => item.ReservedQuantity + quantity)
                    .SetProperty(item => item.LastUpdatedAt, occurredAt),
                cancellationToken);

        return occupiedInventoryRows == 1
            ? ReservationCapacityClaimResult.Succeeded
            : ReservationCapacityClaimResult.InventoryInsufficient;
    }
}
