namespace MaskMap.Api.Domains
{
    public sealed class ReservationOperation
    {
        public string UserId { get; set; } = string.Empty;
        public string IdempotencyKey { get; set; } = string.Empty;
        public string PharmacyId { get; set; } = string.Empty;
        public string ProductId { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public string ReservationId { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
    }
}
