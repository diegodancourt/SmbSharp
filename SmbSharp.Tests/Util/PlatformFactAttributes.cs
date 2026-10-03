using Xunit;

namespace SmbSharp.Tests.Util
{
    public sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute()
        {
            if (!OperatingSystem.IsWindows())
                Skip = "This test exercises the native Windows filesystem path.";
        }
    }

    public sealed class LinuxFactAttribute : FactAttribute
    {
        public LinuxFactAttribute()
        {
            if (!OperatingSystem.IsLinux())
                Skip = "This test requires Linux.";
        }
    }

    public sealed class LinuxSmbClientFactAttribute : FactAttribute
    {
        public LinuxSmbClientFactAttribute()
        {
            if (!OperatingSystem.IsLinux())
            {
                Skip = "This test requires Linux.";
                return;
            }

            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            if (!path.Split(Path.PathSeparator)
                    .Any(directory => File.Exists(Path.Combine(directory, "smbclient"))))
                Skip = "smbclient is not installed.";
        }
    }

    public sealed class UnsupportedPlatformFactAttribute : FactAttribute
    {
        public UnsupportedPlatformFactAttribute()
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                Skip = "This test requires an unsupported operating system.";
        }
    }

    public sealed class ManualSmbFactAttribute : FactAttribute
    {
        public ManualSmbFactAttribute()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("SMBSHARP_RUN_LIVE_TESTS"), "1",
                    StringComparison.Ordinal))
                Skip = "Set SMBSHARP_RUN_LIVE_TESTS=1 to opt in to live SMB integration tests.";
        }
    }
}
