namespace MaskMap.Api.Domains
{
    public class UserQuota
    {
        public string UserId { get; set; }
        public int Limit { get; set; }
        public int ReservedQuantity { get; set; }
        public int PurchasedQuantity { get; set; }
        public byte[] RowVersion { get; private set; } = [];

        public bool CanReserve(int quantity)
        {
            return quantity > 0 &&
                   Limit - ReservedQuantity - PurchasedQuantity >= quantity;
        }

        public void Reserve(int quantity)
        {
            if (!CanReserve(quantity))
            {
                throw new InvalidOperationException("User quota is insufficient for the reservation.");
            }

            ReservedQuantity += quantity;
        }
    }
}
