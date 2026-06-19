namespace MaskMap.Api.Controllers
{
    public sealed record UserReservationsResponse(
        IReadOnlyList<UserReservationResponse> Items);

    public sealed record UserReservationResponse(
        string ReservationId,
        string Status,
        string PharmacyId,
        string ProductId,
        int Quantity,
        DateTimeOffset ExpiresAt);
}
