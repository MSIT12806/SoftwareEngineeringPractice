namespace MaskMap.Api.Domains
{
    public class UserQuota
    {
        public string UserId { get; set; }
        public int Limit { get; set; }
        public int ReservedQuantity { get; set; }
        public int PurchasedQuantity { get; set; }
    }
}
