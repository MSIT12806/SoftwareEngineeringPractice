using MaskMap.Api.Domains;

namespace MaskMap.Api.Controllers
{
    public class PharmacyQueryResponse
    {
        public string PharmacyId { get; set; }
        public string Name { get; set; }
        public decimal DistanceKm { get; set; }
        public IEnumerable<Inventory> Inventories { get; set; }
    }
}
