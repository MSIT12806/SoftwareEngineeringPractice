namespace MaskMap.Api.Domains
{
    public class Inventory
    {
        public string PharmacyId { get; set; }
        public string ProductId { get; set; }
        public int AvailableQuantity { get; set; }
        public int ReservedQuantity { get; set; }
        public int PhysicalQuantity { get; set; }
        public DateTimeOffset LastUpdatedAt { get; set; }
    }
}
