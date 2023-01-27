namespace CreateProcessAsUser.Client.Tests
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            //new UnitTest1().Inherit().ConfigureAwait(false).GetAwaiter().GetResult();
            new UnitTest1().Credentials().ConfigureAwait(false).GetAwaiter().GetResult();
        }
    }
}
