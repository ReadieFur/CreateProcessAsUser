using System.ComponentModel;
using System.Configuration.Install;
using System.ServiceProcess;

namespace CreateProcessAsUser.Service
{
    [RunInstaller(true)]
    public partial class ServiceInstaller : Installer
    {
        public ServiceInstaller()
        {
            InitializeComponent();

            //Set UI display information.
            serviceInstaller1.DisplayName = AssemblyInfo.TITLE;
            serviceInstaller1.Description = AssemblyInfo.DESCRIPTION;

            //Set the service name, this must match the name of the executable.
            serviceInstaller1.ServiceName = AssemblyInfo.TITLE;

            //Set the service privileges.
            serviceProcessInstaller1.Account = ServiceAccount.LocalSystem;

            //Set the service to start up automatically.
            serviceInstaller1.StartType = ServiceStartMode.Automatic;
        }
    }
}
