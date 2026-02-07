using Microsoft.Extensions.Logging;
using SmbSharp.Business;
using SmbSharp.Business.Interfaces;

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});

Console.WriteLine("=== SmbSharp Sample App ===");
Console.WriteLine();

// On Windows, ask if WSL should be used
var useWsl = false;
if (OperatingSystem.IsWindows())
{
    Console.Write("Use smbclient via WSL? (y/N): ");
    useWsl = Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true;
}

// Get authentication mode
Console.Write("Authentication mode (1 = Kerberos, 2 = Username/Password): ");
var authMode = Console.ReadLine()?.Trim();

IFileHandler fileHandler;

if (authMode == "2")
{
    Console.Write("Username: ");
    var username = Console.ReadLine()?.Trim() ?? "";

    Console.Write("Password: ");
    var password = ReadPassword();

    Console.Write("Domain (optional, press Enter to skip): ");
    var domain = Console.ReadLine()?.Trim();

    fileHandler = FileHandler.CreateWithCredentials(
        username,
        password,
        string.IsNullOrEmpty(domain) ? null : domain,
        loggerFactory,
        useWsl);
}
else
{
    fileHandler = FileHandler.CreateWithKerberos(loggerFactory, useWsl);
}

Console.WriteLine();
Console.WriteLine("FileHandler created successfully.");
Console.WriteLine();

while (true)
{
    Console.WriteLine("--- Menu ---");
    Console.WriteLine("1. List files in directory");
    Console.WriteLine("2. Check if file exists");
    Console.WriteLine("3. Read file content");
    Console.WriteLine("4. Write text to file");
    Console.WriteLine("5. Create directory");
    Console.WriteLine("6. Move file");
    Console.WriteLine("7. Delete file");
    Console.WriteLine("8. Test connectivity");
    Console.WriteLine("9. Exit");
    Console.Write("Choice: ");

    var choice = Console.ReadLine()?.Trim();
    Console.WriteLine();

    try
    {
        switch (choice)
        {
            case "1":
                await ListFiles(fileHandler);
                break;
            case "2":
                await CheckFileExists(fileHandler);
                break;
            case "3":
                await ReadFile(fileHandler);
                break;
            case "4":
                await WriteFile(fileHandler);
                break;
            case "5":
                await CreateDirectory(fileHandler);
                break;
            case "6":
                await MoveFile(fileHandler);
                break;
            case "7":
                await DeleteFile(fileHandler);
                break;
            case "8":
                await TestConnectivity(fileHandler);
                break;
            case "9":
                Console.WriteLine("Goodbye!");
                return;
            default:
                Console.WriteLine("Invalid choice.");
                break;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.GetType().Name}: {ex.Message}");
    }

    Console.WriteLine();
}

static async Task ListFiles(IFileHandler handler)
{
    Console.Write("Directory path (e.g., //server/share or //server/share/folder): ");
    var dir = Console.ReadLine()?.Trim() ?? "";

    var files = await handler.EnumerateFilesAsync(dir);
    var list = files.ToList();

    Console.WriteLine($"Found {list.Count} file(s):");
    foreach (var file in list)
    {
        Console.WriteLine($"  {file}");
    }
}

static async Task CheckFileExists(IFileHandler handler)
{
    Console.Write("Directory path: ");
    var dir = Console.ReadLine()?.Trim() ?? "";
    Console.Write("File name: ");
    var fileName = Console.ReadLine()?.Trim() ?? "";

    var exists = await handler.FileExistsAsync(fileName, dir);
    Console.WriteLine(exists ? "File exists." : "File does not exist.");
}

static async Task ReadFile(IFileHandler handler)
{
    Console.Write("Directory path: ");
    var dir = Console.ReadLine()?.Trim() ?? "";
    Console.Write("File name: ");
    var fileName = Console.ReadLine()?.Trim() ?? "";

    await using var stream = await handler.ReadFileAsync(dir, fileName);
    using var reader = new StreamReader(stream);
    var content = await reader.ReadToEndAsync();

    Console.WriteLine($"--- Content ({content.Length} chars) ---");
    Console.WriteLine(content.Length > 2000 ? content[..2000] + "\n... (truncated)" : content);
    Console.WriteLine("--- End ---");
}

static async Task WriteFile(IFileHandler handler)
{
    Console.Write("Full file path (e.g., //server/share/folder/file.txt): ");
    var filePath = Console.ReadLine()?.Trim() ?? "";
    Console.Write("Content to write: ");
    var content = Console.ReadLine() ?? "";

    var result = await handler.WriteFileAsync(filePath, content);
    Console.WriteLine(result ? "File written successfully." : "Write failed.");
}

static async Task CreateDirectory(IFileHandler handler)
{
    Console.Write("Directory path to create (e.g., //server/share/newfolder): ");
    var dir = Console.ReadLine()?.Trim() ?? "";

    var result = await handler.CreateDirectoryAsync(dir);
    Console.WriteLine(result ? "Directory created successfully." : "Create failed.");
}

static async Task MoveFile(IFileHandler handler)
{
    Console.Write("Source file path: ");
    var source = Console.ReadLine()?.Trim() ?? "";
    Console.Write("Destination file path: ");
    var dest = Console.ReadLine()?.Trim() ?? "";

    var result = await handler.MoveFileAsync(source, dest);
    Console.WriteLine(result ? "File moved successfully." : "Move failed.");
}

static async Task DeleteFile(IFileHandler handler)
{
    Console.Write("File path to delete: ");
    var filePath = Console.ReadLine()?.Trim() ?? "";

    var result = await handler.DeleteFileAsync(filePath);
    Console.WriteLine(result ? "File deleted successfully." : "Delete failed.");
}

static async Task TestConnectivity(IFileHandler handler)
{
    Console.Write("Directory path to test: ");
    var dir = Console.ReadLine()?.Trim() ?? "";

    var canConnect = await handler.CanConnectAsync(dir);
    Console.WriteLine(canConnect ? "Connection successful!" : "Connection failed.");
}

static string ReadPassword()
{
    var password = "";
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
        {
            Console.WriteLine();
            break;
        }

        if (key.Key == ConsoleKey.Backspace && password.Length > 0)
        {
            password = password[..^1];
            Console.Write("\b \b");
        }
        else if (!char.IsControl(key.KeyChar))
        {
            password += key.KeyChar;
            Console.Write("*");
        }
    }

    return password;
}
