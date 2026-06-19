namespace MaskMap.Api.Domains
{
    public sealed class ReservationConflictException : Exception
    {
        public ReservationConflictException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }
}
