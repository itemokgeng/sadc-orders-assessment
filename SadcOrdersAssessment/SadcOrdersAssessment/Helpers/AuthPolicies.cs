namespace SadcOrdersAssessment.Helpers
{
    public static class AuthPolicies
    {
        public const string OrdersRead = "Orders.Read";
        public const string OrdersWrite = "Orders.Write";
        public const string OrdersAdmin = "Orders.Admin";

        public static readonly IReadOnlyList<string> AllRoles = new[] { OrdersRead, OrdersWrite, OrdersAdmin };
    }
}
