using System;
using System.Runtime.InteropServices;
using System.Text;

namespace x_pad.Services;

/// <summary>
/// Detects whether the current process is running inside an MSIX / Store package.
/// Unpackaged installs (zip, <c>dotnet run</c>, tests) always return false.
/// </summary>
public static class PackageIdentity
{
    // Win32 error codes from GetCurrentPackageFullName (not HRESULTs).
    private const int AppModelErrorNoPackage = 15700;
    private const int ErrorInsufficientBuffer = 122;

    /// <summary>
    /// True when this process has package identity (MSIX / Microsoft Store install).
    /// </summary>
    public static bool IsPackaged()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            uint length = 0;
            var rc = GetCurrentPackageFullName(ref length, null);
            if (rc == AppModelErrorNoPackage)
                return false;
            // ERROR_INSUFFICIENT_BUFFER means a package name exists and needs a buffer.
            return rc == ErrorInsufficientBuffer || rc == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Full package name when packaged; otherwise null.
    /// </summary>
    public static string? GetFullName()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            uint length = 0;
            var rc = GetCurrentPackageFullName(ref length, null);
            if (rc == AppModelErrorNoPackage || length == 0)
                return null;

            var sb = new StringBuilder((int)length);
            rc = GetCurrentPackageFullName(ref length, sb);
            return rc == 0 ? sb.ToString() : null;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, StringBuilder? packageFullName);
}
