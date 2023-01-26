using System.Diagnostics;

namespace CreateProcessAsUser.Service.Installer
{
    internal class Program
    {
        private const string SERVICE_NAME = "CreateProcessAsUser.Service";

        static void Main(string[] args)
        {
            string servicePath = args.Length >= 1 && File.Exists(args[0])
                ? args[0] : Path.Combine(Environment.CurrentDirectory, SERVICE_NAME);

            string queryResult = sc($"query {SERVICE_NAME}");

            if (queryResult.Contains("The specified service does not exist as an installed service."))
            {
                //Install.
                Console.WriteLine("Installing service...");
                string result = sc($"create {SERVICE_NAME}"
                    + $" type=own"
                    + $" start=auto"
                    + $" binpath=\"{servicePath}\""
                    + $" obj=LocalSystem"
                    + $" displayname={SERVICE_NAME}");
                Console.WriteLine(result);

                sc($"start {SERVICE_NAME}");

                sc($"description {SERVICE_NAME} \"Runs processes in a specified user space.\"");
            }
            else
            {
                //Uninstall.
                Console.WriteLine("Uninstalling service...");
                sc($"stop {SERVICE_NAME}");
                Console.WriteLine(sc($"delete {SERVICE_NAME}"));
            }
        }

        static string sc(string args)
        {
            Process sc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = args,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    Verb = "runas"
                }
            };
            sc.Start();
            sc.WaitForExit();
            return sc.StandardOutput.ReadToEnd();
        }
    }
}
