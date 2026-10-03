namespace SmbSharp.Business.SmbClient
{
    internal interface ISmbClientFileHandlerMove
    {
        Task RenameFileAsync(string sourceDirectory, string sourceFileName, string destinationDirectory,
            string destinationFileName, CancellationToken cancellationToken);
    }
}
