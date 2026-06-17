namespace MaskMap.Api.Domains
{
    public class Reservation
    {
        public string ReservationId { get; set; }
        public string Status { get; set; }
        public string PharmacyId { get; set; }
        public string ProductId { get; set; }
        public int Quantity { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
