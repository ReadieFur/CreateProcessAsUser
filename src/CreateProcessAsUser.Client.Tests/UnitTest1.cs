using System.Security.Principal;
using System.Threading.Tasks;
using CreateProcessAsUser.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CreateProcessAsUser.Client.Tests
{
    [TestClass]
    public class UnitTest1
    {
        private static readonly SProcessInformation processInformation = new()
        {
            executablePath = "C:\\Windows\\System32\\calc.exe".ToCharArray(),
        };

        [TestMethod]
        public async Task Inherit()
        {
            SParameters parameters = new()
            {
                authenticationMode = EAuthenticationMode.INHERIT,
                processInformation = processInformation,
            };

            SResult result = await Helper.CreateProcessAsUser(parameters/*, TimeSpan.FromSeconds(5)*/);

            Assert.IsTrue(result.result == EResult.CREATED_PROCESS);
        }

        [TestMethod]
        public async Task Credentials()
        {
            SParameters parameters = new()
            {
                authenticationMode = EAuthenticationMode.CREDENTIALS,
                processInformation = processInformation,
                credentials = new()
                {
                    username = Secretes.username.ToCharArray(),
                    password = Secretes.password.ToCharArray(),
                    domain = WindowsIdentity.GetCurrent().Name.Split('\\')[0].ToCharArray(),
                },
            };

            SResult result = await Helper.CreateProcessAsUser(parameters/*, TimeSpan.FromSeconds(5)*/);

            Assert.IsTrue(result.result == EResult.CREATED_PROCESS);
        }
    }
}
