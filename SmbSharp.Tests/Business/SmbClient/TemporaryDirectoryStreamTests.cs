using SmbSharp.Business.SmbClient;

namespace SmbSharp.Tests.Business.SmbClient
{
    public class TemporaryDirectoryStreamTests
    {
        [Fact]
        public async Task DisposeAsync_ClosesStreamAndRemovesTemporaryDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"smbsharp-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var filePath = Path.Combine(directory, "download.tmp");
            await File.WriteAllTextAsync(filePath, "temporary content");
            var stream = new TemporaryDirectoryStream(File.OpenRead(filePath), directory);

            try
            {
                await stream.DisposeAsync();

                Assert.False(Directory.Exists(directory));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }
    }
}
