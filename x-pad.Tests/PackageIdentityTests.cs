using x_pad.Services;

namespace x_pad.Tests;

public class PackageIdentityTests
{
    [Fact]
    public void IsPackaged_IsFalse_UnderTestHost()
    {
        // The xUnit host is unpackaged; APPMODEL_ERROR_NO_PACKAGE → false.
        Assert.False(PackageIdentity.IsPackaged());
    }

    [Fact]
    public void GetFullName_IsNull_UnderTestHost()
    {
        Assert.Null(PackageIdentity.GetFullName());
    }
}
