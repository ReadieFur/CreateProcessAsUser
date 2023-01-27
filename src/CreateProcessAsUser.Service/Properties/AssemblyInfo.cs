using System.Reflection;
using System.Runtime.InteropServices;

// General Information about an assembly is controlled through the following
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle(AssemblyInfo.TITLE)]
[assembly: AssemblyDescription(AssemblyInfo.DESCRIPTION)]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany(AssemblyInfo.COMPANY)]
[assembly: AssemblyProduct(AssemblyInfo.TITLE)]
[assembly: AssemblyCopyright(AssemblyInfo.COPYRIGHT)]
[assembly: AssemblyTrademark(AssemblyInfo.TRADEMARK)]
[assembly: AssemblyCulture("")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(AssemblyInfo.COM_VISIBLE)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid(AssemblyInfo.GUID)]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version
//      Build Number
//      Revision
//
// You can specify all the values or you can default the Build and Revision Numbers
// by using the '*' as shown below:
// [assembly: AssemblyVersion("1.0.*")]
[assembly: AssemblyVersion(AssemblyInfo.VERSION)]
[assembly: AssemblyFileVersion(AssemblyInfo.VERSION)]

public static class AssemblyInfo
{
    public const string TITLE = "CreateProcessAsUser.Service";
    public const string DESCRIPTION = "Runs processes in a specified user space.";
    public const string COMPANY = "ReadieFur";
    public const string COPYRIGHT = "Copyright © 2023";
    public const string TRADEMARK = "GPL-3.0 license";
    public const bool COM_VISIBLE = false;
    public const string GUID = "647763f0-b345-477c-83fb-448ce53dce66";
    public const string VERSION = "1.0.0.0";
}
