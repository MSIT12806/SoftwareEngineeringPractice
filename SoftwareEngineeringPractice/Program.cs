// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");
var ut = new UnitTests_v3();
ut.RegularCustomer_ShouldPay99PercentOfOriginalAmount();
ut.VipCustomer_ShouldPay90PercentOfOriginalAmount();
ut.BirthMonth_ShouldPay95PercentOfOriginalAmount();
ut.Employee_ShouldPay85PercentOfOriginalAmount();
var pt = new ProcessTests_v3();
pt.VipCustomerInBirthMonth_ShouldApplyVipThenBirthdayDiscount();
pt.Emplyee_ShouldGetVipCustomerDiscountAndBirthMonthDiscount();
pt.Emplyee_ShouldGetVipCustomerDiscount();
pt.Emplyee_ShouldNotGetGeneralCustomerDiscount();