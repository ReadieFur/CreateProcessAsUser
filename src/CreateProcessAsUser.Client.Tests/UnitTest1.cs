using System;
using System.Threading.Tasks;
using CreateProcessAsUser.Shared;
using CSharpTools.Pipes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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
                //authenticationMode = EAuthenticationMode.INHERIT,
                authenticationMode = EAuthenticationMode.CREDENTIALS,
                processInformation = new()
                {
                    //executablePath = "C:\\Windows\\System32\\notepad.exe",
                    //workingDirectory = "C:\\Windows\\System32"
                }
            };

            SResult result = await Helper.CreateProcessAsUser(parameters, TimeSpan.FromSeconds(5));

            Assert.IsTrue(result.result == EResult.OK);
        }
    }
}
