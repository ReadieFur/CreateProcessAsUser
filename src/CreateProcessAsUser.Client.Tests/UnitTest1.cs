using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CreateProcessAsUser.Shared;

namespace CreateProcessAsUser.Client.Tests
{
    [TestClass]
    public class UnitTest1
    {
        [TestMethod]
        public async Task Inherit()
        {
            SParameters parameters = new()
            {
                authenticationMode = EAuthenticationMode.INHERIT
            };

            SResult result = await Helper.CreateProcessAsUser(parameters, TimeSpan.FromSeconds(5));

            Assert.IsTrue(result.result == EResult.OK);
        }
    }
}
