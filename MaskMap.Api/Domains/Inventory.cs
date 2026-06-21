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
        public byte[] RowVersion { get; private set; } = [];

        public bool CanReserve(int quantity)
        {
            return quantity > 0 && AvailableQuantity >= quantity;
        }

        public void Reserve(int quantity, DateTimeOffset occurredAt)
        {
            if (!CanReserve(quantity))
            {
                throw new InvalidOperationException("Inventory is insufficient for the reservation.");
            }

            AvailableQuantity -= quantity;
            ReservedQuantity += quantity;
            LastUpdatedAt = occurredAt;
        }
    }
}
