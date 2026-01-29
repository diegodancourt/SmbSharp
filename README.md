# SmbSharp

A cross-platform .NET library for SMB/CIFS file operations. Works seamlessly on Windows using native UNC paths and on Linux using smbclient.

[![NuGet](https://img.shields.io/nuget/v/SmbSharp.svg)](https://www.nuget.org/packages/SmbSharp/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## Features

- ✅ **Cross-Platform**: Windows (native UNC) and Linux (smbclient)
- ✅ **Dual Authentication**: Kerberos and username/password authentication
- ✅ **Stream-Based API**: Efficient, memory-friendly file operations
- ✅ **Async/Await**: Full async support with cancellation tokens
- ✅ **Dependency Injection**: Built-in ASP.NET Core DI integration
- ✅ **Health Checks**: Monitor SMB share connectivity with ASP.NET Core health checks
- ✅ **Multiple .NET Versions**: Supports .NET Core 3.1, .NET 6, .NET 8, and .NET 10
- ✅ **Secure**: Passwords passed via environment variables, not command-line arguments
- ✅ **Well-Documented**: Comprehensive XML documentation with IntelliSense support

## Installation

### NuGet Package Manager
```bash
Install-Package SmbSharp
```

### .NET CLI
```bash
dotnet add package SmbSharp
```

### Package Reference
```xml
<PackageReference Include="SmbSharp" Version="1.0.0" />
```

## Platform Requirements

### Windows
- No additional requirements - uses native UNC path support

### Linux
- Requires `smbclient` to be installed:
  ```bash
  # Debian/Ubuntu
  sudo apt-get install smbclient

  # RHEL/CentOS
  sudo yum install samba-client

  # Alpine Linux
  apk add samba-client
  ```

### Docker
Add smbclient to your Dockerfile:

**Debian/Ubuntu-based images:**
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0
RUN apt-get update && apt-get install -y smbclient && rm -rf /var/lib/apt/lists/*
```

**Alpine-based images:**
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine
RUN apk add --no-cache samba-client
```

**RHEL/CentOS-based images:**
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0-rhel
RUN yum install -y samba-client && yum clean all
```

## Quick Start

### Using Dependency Injection (Recommended)

#### Kerberos Authentication (Default)
```csharp
// Program.cs
builder.Services.AddSmbSharp();

// Usage in a controller/service
public class MyService
{
    private readonly IFileHandler _fileHandler;

    public MyService(IFileHandler fileHandler)
    {
        _fileHandler = fileHandler;
    }

    public async Task<IEnumerable<string>> GetFiles()
    {
        return await _fileHandler.EnumerateFilesAsync("//server/share/folder");
    }
}
```

#### Username/Password Authentication
```csharp
// Program.cs - Direct credentials
builder.Services.AddSmbSharp("username", "password", "DOMAIN");

// Or using configuration
builder.Services.AddSmbSharp(options =>
{
    options.UseKerberos = false;
    options.Username = "username";
    options.Password = "password";
    options.Domain = "DOMAIN";
});

// Or from appsettings.json
builder.Services.AddSmbSharp(options =>
{
    builder.Configuration.GetSection("SmbSharp").Bind(options);
});
```

### Direct Instantiation

```csharp
// Kerberos authentication
var handler = new FileHandler();

// Username/password authentication
var handler = new FileHandler("username", "password", "DOMAIN");
```

## Path Format

SmbSharp accepts SMB paths in multiple formats for flexibility:

```csharp
// Forward slashes (recommended for cross-platform code)
await fileHandler.EnumerateFilesAsync("//server/share/folder");

// Backslashes (Windows UNC format)
await fileHandler.EnumerateFilesAsync("\\\\server\\share\\folder");

// Mixed (automatically normalized)
await fileHandler.EnumerateFilesAsync("//server/share\\folder");
```

**Note:** All path formats are automatically normalized internally. Forward slashes (`/`) are recommended for cross-platform compatibility, but backslashes (`\`) are fully supported for Windows-style UNC paths.

## Usage Examples

### List Files in a Directory
```csharp
var files = await fileHandler.EnumerateFilesAsync("//server/share/folder");
foreach (var file in files)
{
    Console.WriteLine(file);
}
```

### Read a File
```csharp
await using var stream = await fileHandler.ReadFileAsync("//server/share/folder", "file.txt");
using var reader = new StreamReader(stream);
var content = await reader.ReadToEndAsync();
```

### Write a File (String Content)
```csharp
await fileHandler.WriteFileAsync("//server/share/folder/file.txt", "Hello, World!");
```

### Write a File (Stream)
```csharp
await using var fileStream = File.OpenRead("local-file.txt");
await fileHandler.WriteFileAsync("//server/share/folder/file.txt", fileStream);
```

### Write with Different Modes
```csharp
// Overwrite existing file (default)
await fileHandler.WriteFileAsync("//server/share/file.txt", stream, FileWriteMode.Overwrite);

// Create only if doesn't exist (fails if exists)
await fileHandler.WriteFileAsync("//server/share/file.txt", stream, FileWriteMode.CreateNew);

// Append to existing file
await fileHandler.WriteFileAsync("//server/share/file.txt", stream, FileWriteMode.Append);
```

### Delete a File
```csharp
await fileHandler.DeleteFileAsync("//server/share/folder/file.txt");
```

### Move a File
```csharp
await fileHandler.MoveFileAsync(
    "//server/share/folder/old.txt",
    "//server/share/folder/new.txt"
);
```

> **Note:** On Linux, move operations download and re-upload the file, which can be slow for large files.

### Create a Directory
```csharp
await fileHandler.CreateDirectoryAsync("//server/share/newfolder");
```

### Test Connectivity
```csharp
bool canConnect = await fileHandler.CanConnectAsync("//server/share");
if (canConnect)
{
    Console.WriteLine("Successfully connected!");
}
```

### Using Cancellation Tokens
```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

try
{
    var files = await fileHandler.EnumerateFilesAsync(
        "//server/share/folder",
        cts.Token
    );
}
catch (OperationCanceledException)
{
    Console.WriteLine("Operation timed out!");
}
```

## Authentication

### Kerberos Authentication
On Linux, ensure you have a valid Kerberos ticket before using the library:
```bash
kinit username@DOMAIN.COM
```

Verify your ticket:
```bash
klist
```

### Username/Password Authentication
Credentials are securely passed to smbclient via environment variables, not command-line arguments, preventing exposure in process listings.

## Health Checks

SmbSharp includes built-in health check support for ASP.NET Core applications to monitor SMB share connectivity.

### Single Share Health Check

```csharp
// Program.cs
builder.Services.AddSmbSharp();
builder.Services.AddHealthChecks()
    .AddSmbShareCheck("//server/share/folder");
```

### Named Health Check with Options

```csharp
builder.Services.AddHealthChecks()
    .AddSmbShareCheck(
        directoryPath: "//server/share/folder",
        name: "primary_smb_share",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "smb", "storage" },
        timeout: TimeSpan.FromSeconds(10)
    );
```

### Multiple Share Health Checks

```csharp
var shares = new Dictionary<string, string>
{
    { "primary", "//server1/share1" },
    { "backup", "//server2/share2" },
    { "archive", "//server3/share3" }
};

builder.Services.AddHealthChecks()
    .AddSmbShareChecks(shares, tags: new[] { "smb" });
```

### Health Check Endpoint

```csharp
// Program.cs
var app = builder.Build();

app.MapHealthChecks("/health");
// Or with detailed response
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});
```

### Example Response

**Healthy:**
```json
{
  "status": "Healthy",
  "results": {
    "smb_share": {
      "status": "Healthy",
      "description": "Successfully connected to SMB share: //server/share/folder"
    }
  }
}
```

**Unhealthy:**
```json
{
  "status": "Unhealthy",
  "results": {
    "smb_share": {
      "status": "Unhealthy",
      "description": "Unable to connect to SMB share: //server/share/folder"
    }
  }
}
```

## API Reference

### IFileHandler Interface

| Method | Description |
|--------|-------------|
| `EnumerateFilesAsync(directory, cancellationToken)` | Lists all files in a directory |
| `ReadFileAsync(directory, fileName, cancellationToken)` | Opens a file for reading as a stream |
| `WriteFileAsync(filePath, content, cancellationToken)` | Writes a string to a file |
| `WriteFileAsync(filePath, stream, cancellationToken)` | Writes a stream to a file |
| `WriteFileAsync(filePath, stream, writeMode, cancellationToken)` | Writes a stream with specific write mode |
| `DeleteFileAsync(filePath, cancellationToken)` | Deletes a file |
| `MoveFileAsync(sourcePath, destPath, cancellationToken)` | Moves a file |
| `CreateDirectoryAsync(directoryPath, cancellationToken)` | Creates a directory |
| `CanConnectAsync(directoryPath, cancellationToken)` | Tests connectivity to a share |

### FileWriteMode Enum

| Value | Description |
|-------|-------------|
| `Overwrite` | Creates a new file or overwrites existing (default) |
| `CreateNew` | Creates only if file doesn't exist (throws if exists) |
| `Append` | Appends to existing file or creates new |

## Error Handling

The library throws specific exceptions for different error scenarios:

```csharp
try
{
    await fileHandler.ReadFileAsync("//server/share", "file.txt");
}
catch (FileNotFoundException ex)
{
    // File or path doesn't exist
}
catch (UnauthorizedAccessException ex)
{
    // Access denied or authentication failed
}
catch (DirectoryNotFoundException ex)
{
    // Network path not found
}
catch (IOException ex)
{
    // Other SMB/network errors
}
catch (PlatformNotSupportedException ex)
{
    // Running on unsupported platform (not Windows/Linux)
}
```

## Performance Considerations

### Windows
- Uses native UNC paths - very efficient
- All operations are direct file system calls
- Move operations are atomic and instant (metadata-only)

### Linux
- Uses smbclient subprocess - some overhead
- Read operations download to temp file (auto-cleaned)
- Write operations upload from temp file
- Move operations = download + upload + delete (can be slow for large files)
- For large files, consider alternative approaches or be aware of 2x disk space + network transfer

## Security Features

- ✅ Passwords passed via environment variables (not visible in process listings)
- ✅ Command injection protection (input escaping)
- ✅ Comprehensive input validation
- ✅ Path traversal protection

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Support

- **Issues**: [GitHub Issues](https://github.com/diegodancourt/SmbSharp/issues)
- **Discussions**: [GitHub Discussions](https://github.com/diegodancourt/SmbSharp/discussions)
- **Email**: diego@dancourt.org

## Acknowledgments

Built with ❤️ for the .NET community.
