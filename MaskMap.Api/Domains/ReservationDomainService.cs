using MaskMap.Api.Controllers;

namespace MaskMap.Api.Domains
{
    public class ReservationDomainService
    {
        private readonly IDb _db;
        public Reservation GetById(string id)
        {
            throw new NotImplementedException();
        }
        public Reservation Create(ReservationRequest req)
        {
            //拿到該藥局口罩庫存，確認庫存數量可以進行預約。
            //建立預約物件
            throw new NotImplementedException();
        }
    }
}
