using System.Runtime.CompilerServices;

public class UnitTestBase
{

    protected ExpectObj Expect(object v)
    {
        return new ExpectObj { Value = v };
    }

    protected class ExpectObj
    {
        public object Value { get; set; }

        public void Equal(object result, [CallerMemberName] string testName = "")
        {
            Console.WriteLine($"Excute {testName} Test");
            if (Value.Equals(result))
            {
                Console.WriteLine("Pass");
                return;
            }

            Console.WriteLine("Fail");
        }
    }
}
