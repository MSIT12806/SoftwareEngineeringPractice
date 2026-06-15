using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SoftwareEngineeringPractice
{
    /*  Spec
     *  某個電商系統原本只有一般會員與 VIP 會員，產品經理說，未來可能加入：
        - 生日會員折扣
        - 員工折扣
        - 節慶活動折扣
        - 不同國家的折扣政策
        - 可同時套用多種折扣，但有些折扣不能併用
     */
    public class AmountCalculatHandler
    {
        public decimal CalculateDiscount(
    Customer customer,
    decimal totalAmount)
        {
            if (customer.Type == CustomerType.Regular)
            {
                return totalAmount * 0.05m;
            }

            if (customer.Type == CustomerType.Vip)
            {
                return totalAmount * 0.10m;
            }

            return 0;
        }
    }

    public enum CustomerType
    {
        Regular,
        Vip
    }

    public class Customer
    {
        public CustomerType Type { get; set; }
    }
}
