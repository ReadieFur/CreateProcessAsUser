using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CreateProcessAsUser.Client.Tests
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            new UnitTest1().Inherit().ConfigureAwait(false).GetAwaiter().GetResult();
        }
    }
}
