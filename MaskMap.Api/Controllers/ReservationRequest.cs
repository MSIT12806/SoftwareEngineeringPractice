namespace MaskMap.Api.Controllers
{
    public class ReservationRequest
    {
        public string UserId { get; set; }
        public string PharmacyId { get; set; }
        public string ProductId { get; set; }
        public int Quantity { get; set; }
    }
}
