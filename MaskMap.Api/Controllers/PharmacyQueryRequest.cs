namespace MaskMap.Api.Controllers
{
    public class PharmacyQueryRequest
    {
        public decimal Lat { get; set; }
        public decimal Lng { get; set; }
        public decimal RadiusKm { get; set; }
    }
}
