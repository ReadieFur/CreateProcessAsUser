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

            var bs = Properties.BUFFER_SIZE;
            SResult result = await Helper.CreateProcessAsUser(parameters);

            Assert.IsTrue(result.result == EResult.OK);
        }
    }
}
