using System.Globalization;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace TrailFtms.Tests;

[TestClass]
public static class TestSetup
{
    // Same culture as the app (Program.cs), whatever the machine running the tests.
    [AssemblyInitialize]
    public static void SetCulture(TestContext _)
    {
        var culture = CultureInfo.GetCultureInfo("fr-FR");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
