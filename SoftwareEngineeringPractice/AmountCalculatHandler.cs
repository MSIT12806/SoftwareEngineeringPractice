using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using static AmountCalculatHandler_v2;

/*  Spec
 *  電商系統在結帳時，需要依據顧客身分與活動條件計算可折抵金額。
 *
 *  目前已知規則：
 *  - 一般會員可折抵訂單總金額的 1%。✔️✔️
 *  - VIP 會員可折抵訂單總金額的 10%。✔️✔️
 *
 *  產品經理已提出未來可能加入的折扣情境：
 *  - 生日會員折扣：顧客生日月份可取得額外折扣。(5%)✔️✔️
 *  - 員工折扣：員工身分可能有固定比例折扣(15%)。✔️✔️
 *      - 若已計算員工折扣，就不再計算一般會員折扣✔️✔️
 *  - 節慶活動折扣：特定活動期間可能套用活動折扣。
 *      - 滿足某種消費模式，可進行百分比折扣(三件15%)、(特定商品 40%)
 *      - 總折扣：所有折扣計算完畢的總金額，若滿足一定金額，可進行額外折扣(ex: 滿千送百)
 *  - 多重折扣：部分折扣可同時套用，部分折扣彼此互斥，需要有明確的併用規則。
 *      - 節慶活動，單一訂單明細若同時符合多個節慶活動，僅套用對該明細產生實際折抵金額最高的一項活動，不得疊加多個節慶活動折扣。
 *
 *  設計目標：
 *  - 新增折扣類型時，應盡量避免反覆修改既有的折扣計算流程。
 *  - 折扣規則應能被獨立測試，而不是全部集中在單一 if/else 或 switch 中。
 *  - 計算結果應回傳「折抵金額」，不是折扣後的應付總額。
 */

#region v1
public class AmountCalculatHandler_v1
{
    public decimal CalculateDiscount(
Customer customer,
decimal totalAmount)
    {
        if (customer.Type == CustomerType.Regular)
        {
            return totalAmount * 0.99m;
        }

        if (customer.Type == CustomerType.Vip)
        {
            return totalAmount * 0.90m;
        }

        return totalAmount;
    }
}

public class UnitTests_v1 : UnitTestBase
{
    public void RegularCustomer_ShouldPay99PercentOfOriginalAmount()
    {
        var amountHandler = new AmountCalculatHandler_v1();
        var customer = new Customer
        {
            Type = CustomerType.Regular,
        };
        decimal payableRate = 0.99M;
        decimal amount = 1000;

        var result = amountHandler.CalculateDiscount(customer, amount);
        Expect(amount * payableRate).Equal(result);
    }

    public void VipCustomer_ShouldPay90PercentOfOriginalAmount()
    {
        var amountHandler = new AmountCalculatHandler_v1();
        var customer = new Customer
        {
            Type = CustomerType.Vip,
        };
        decimal payableRate = 0.90M;
        decimal amount = 1000;

        var result = amountHandler.CalculateDiscount(customer, amount);
        Expect(amount * payableRate).Equal(result);
    }
}
#endregion v1

#region v2
public class AmountCalculatHandler_v2
{
    /*
     * 這段重構需要考慮的是，如何乾淨的測試到 BirthMonth。
     */

    public AmountCalculatHandler_v2()
    {
    }

    public decimal CalculateDiscount(
Customer customer,
decimal totalAmount)
    {
        decimal payableAmount = customer.Type switch
        {
            CustomerType.Regular => totalAmount * 0.99m,
            CustomerType.Vip => totalAmount * 0.90m,
            _ => totalAmount
        };

        BirthdayDiscount _birthdayDiscount = new BirthdayDiscount();
        payableAmount = _birthdayDiscount.Apply(
            customer,
            payableAmount);

        return payableAmount;
    }
}

public class UnitTests_v2 : UnitTestBase
{
    public void BirthMonth_ShouldPay95PercentOfOriginalAmount()
    {
        var customer = new Customer
        {
            IsBirthMonth = true
        };

        var discount = new BirthdayDiscount();
        var result = discount.Apply(customer, 1000m);

        Expect(950m).Equal(result);
    }
    public void RegularCustomer_ShouldPay99PercentOfOriginalAmount()
    {
        var amountHandler = new AmountCalculatHandler_v2();
        var customer = new Customer
        {
            Type = CustomerType.Regular,
        };
        decimal payableRate = 0.99M;
        decimal amount = 1000;

        var result = amountHandler.CalculateDiscount(customer, amount);
        Expect(amount * payableRate).Equal(result);
    }
    public void VipCustomer_ShouldPay90PercentOfOriginalAmount()
    {
        var amountHandler = new AmountCalculatHandler_v2();
        var customer = new Customer
        {
            Type = CustomerType.Vip,
        };
        decimal payableRate = 0.90M;
        decimal amount = 1000;

        var result = amountHandler.CalculateDiscount(customer, amount);
        Expect(amount * payableRate).Equal(result);
    }
}

public class ProcessTests_v2 : UnitTestBase
{
    public void VipCustomerInBirthMonth_ShouldApplyVipThenBirthdayDiscount()
    {
        var customer = new Customer
        {
            Type = CustomerType.Vip,
            IsBirthMonth = true,
        };

        var process = new AmountCalculatHandler_v2();
        var result = process.CalculateDiscount(customer, 1000m);
        Expect(1000m * 0.9m * 0.95m).Equal(result);
    }
}

#endregion v2

#region v3
public class AmountCalculatHandler_v3
{
    /*
     * 這段重構需要考慮的是，如何乾淨的測試到 Employee，以及 Employee 跟一般會員的影響。
     */

    public AmountCalculatHandler_v3()
    {
    }

    public decimal CalculateDiscount(
Customer customer,
decimal totalAmount)
    {
        CustomerDiscount customerDiscount = new CustomerDiscount();
        decimal payableAmount = customerDiscount.Apply(customer, totalAmount);

        EmplyeeDiscount emplyeeDiscount = new EmplyeeDiscount();
        payableAmount = emplyeeDiscount.Apply(customer, payableAmount);

        BirthdayDiscount birthdayDiscount = new BirthdayDiscount();
        payableAmount = birthdayDiscount.Apply(
            customer,
            payableAmount);

        return payableAmount;
    }
}

public class UnitTests_v3 : UnitTestBase
{
    public void Employee_ShouldPay85PercentOfOriginalAmount()
    {
        var customer = new Customer
        {
            IsEmplyee = true,
        };

        var discount = new EmplyeeDiscount();
        var result = discount.Apply(customer, 1000m);
        Expect(1000m * 0.85m).Equal(result);
    }
    public void BirthMonth_ShouldPay95PercentOfOriginalAmount()
    {
        var customer = new Customer
        {
            IsBirthMonth = true
        };

        var discount = new BirthdayDiscount();
        var result = discount.Apply(customer, 1000m);

        Expect(950m).Equal(result);
    }
    public void RegularCustomer_ShouldPay99PercentOfOriginalAmount()
    {
        var amountHandler = new AmountCalculatHandler_v3();
        var customer = new Customer
        {
            Type = CustomerType.Regular,
        };
        decimal payableRate = 0.99M;
        decimal amount = 1000;

        var result = amountHandler.CalculateDiscount(customer, amount);
        Expect(amount * payableRate).Equal(result);
    }
    public void VipCustomer_ShouldPay90PercentOfOriginalAmount()
    {
        var amountHandler = new AmountCalculatHandler_v3();
        var customer = new Customer
        {
            Type = CustomerType.Vip,
        };
        decimal payableRate = 0.90M;
        decimal amount = 1000;

        var result = amountHandler.CalculateDiscount(customer, amount);
        Expect(amount * payableRate).Equal(result);
    }
}

public class ProcessTests_v3 : UnitTestBase
{
    public void Emplyee_ShouldGetVipCustomerDiscountAndBirthMonthDiscount() {
        var c = new Customer
        {
            Type = CustomerType.Vip,
            IsEmplyee = true,
            IsBirthMonth = true,
        };

        var process = new AmountCalculatHandler_v3();
        var result = process.CalculateDiscount(c, 1000m);
        Expect(1000m * 0.9m * 0.85m * 0.95m).Equal(result);
    }
    public void Emplyee_ShouldGetVipCustomerDiscount() {

        var c = new Customer
        {
            Type = CustomerType.Vip,
            IsEmplyee = true,
            IsBirthMonth = false,
        };

        var process = new AmountCalculatHandler_v3();
        var result = process.CalculateDiscount(c, 1000m);
        Expect(1000m * 0.9m * 0.85m ).Equal(result);
    }
    public void Emplyee_ShouldNotGetGeneralCustomerDiscount()
    {

        var c = new Customer
        {
            Type = CustomerType.Regular,
            IsEmplyee = true,
            IsBirthMonth = false,
        };

        var process = new AmountCalculatHandler_v3();
        var result = process.CalculateDiscount(c, 1000m);
        Expect(1000m * 0.85m).Equal(result);
    }
    public void VipCustomerInBirthMonth_ShouldApplyVipThenBirthdayDiscount()
    {
        var customer = new Customer
        {
            Type = CustomerType.Vip,
            IsBirthMonth = true,
        };

        var process = new AmountCalculatHandler_v3();
        var result = process.CalculateDiscount(customer, 1000m);
        Expect(1000m * 0.9m * 0.95m).Equal(result);
    }
}
#endregion v3
#region basic
public class CustomerDiscount
{
    public decimal Apply(Customer customer, decimal totalAmount)
    {
        switch (customer.Type)
        {
            case CustomerType.Regular:
                if (customer.IsEmplyee)
                {
                    return totalAmount;
                }
                return totalAmount * 0.99m;

            case CustomerType.Vip:
                return totalAmount * 0.9m;

            default:
                return totalAmount;
        }
    }
}
public class EmplyeeDiscount
{
    public decimal Apply(Customer customer, decimal amount)
    {
        if (customer.IsEmplyee)
        {
            return amount * 0.85m;
        }

        return amount;
    }
}
public class BirthdayDiscount
{
    public decimal Apply(Customer customer, decimal amount)
    {
        return customer.IsBirthMonth
            ? amount * 0.95m
            : amount;
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
    public bool IsBirthMonth { get; internal set; }
    public bool IsEmplyee { get; internal set; }
}

#endregion
