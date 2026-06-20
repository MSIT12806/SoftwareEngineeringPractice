namespace MaskMap.Api.Domains;

public interface IReservationCapacityClaimer
{
    Task<ReservationCapacityClaimResult> TryClaimAsync(
        string userId,
        string pharmacyId,
        string productId,
        int quantity,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken);
}

public enum ReservationCapacityClaimResult
{
    Succeeded,
    QuotaExceeded,
    InventoryInsufficient
}
