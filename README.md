# SmbSharp

A cross-platform .NET library for SMB/CIFS file operations. Works seamlessly on Windows using native UNC paths (or smbclient via WSL), and on Linux/macOS using smbclient.

[![NuGet](https://img.shields.io/nuget/v/SmbSharp.svg)](https://www.nuget.org/packages/SmbSharp/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## Features

- ✅ **Cross-Platform**: Windows (native UNC), Linux (smbclient), and macOS (smbclient)
- ✅ **WSL Support**: Optionally use smbclient via WSL on Windows
- ✅ **Persistent Session Pooling**: Optionally reuse a small pool of authenticated smbclient sessions per share, avoiding a full connect/negotiate/Kerberos-or-NTLM handshake on every call
- ✅ **Directory Enumeration**: List subdirectories, not just files
- ✅ **Extended File Metadata**: Retrieve creation/access/write/change times, attributes, and alternate data streams
- ✅ **Dual Authentication**: Kerberos and username/password authentication
- ✅ **Stream-Based API**: Efficient, memory-friendly file operations
- ✅ **Async/Await**: Full async support with cancellation tokens
- ✅ **Dependency Injection**: Built-in ASP.NET Core DI integration
- ✅ **Health Checks**: Monitor SMB share connectivity with ASP.NET Core health checks
- ✅ **Multiple .NET Versions**: Supports .NET Core 3.1, .NET 6, .NET 8, and .NET 10
- ✅ **Secure**: Passwords passed via environment variables, not command-line arguments
- ✅ **Well-Documented**: Comprehensive XML documentation with IntelliSense support

## What's New in 2.1.0

Security and reliability release:

- **Security**: remote paths and file names are validated and quoted to prevent smbclient command
  injection; passwords are passed via the child process environment instead of credentials files.
- **Session pooling**: sessions are invalidated on cancellation or timeout, commands have a timeout
  and are never replayed after dispatch, and disposal and idle-eviction races are fixed.
  Session initialization is bounded with retry/backoff, and idle authenticated sessions are reused.
- **Moves**: same-share moves use a server-side rename; moves never overwrite an existing destination.
- **WSL / macOS**: new `WslDistribution` option and BSD/macOS `script` support.
- **Username/password logins**: Kerberos is explicitly disabled to avoid slow discovery delays.

Existing target frameworks and public APIs are unchanged.

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
<PackageReference Include="SmbSharp" Version="2.1.0" />
```

## Platform Requirements

### Windows
- No additional requirements - uses native UNC path support
- **Optional WSL support**: If you want to use smbclient via WSL instead of native UNC paths, install WSL and smbclient inside your distribution:
  ```bash
  wsl apt-get install smbclient
  ```

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

### macOS
- Requires `smbclient` to be installed:
  ```bash
  brew install samba
  ```

### Session Pooling Requirement (`script` / `util-linux`)
When `UseSessionPool = true`, smbclient is invoked through the `script` utility (part of
`util-linux`) to allocate a pseudo-terminal, since smbclient never prints its interactive prompt
otherwise. `script` ships by default on virtually all Linux distributions and macOS, so this
usually requires no extra installation - but minimal container base images (e.g. Alpine, or
distroless-style images) may need it added explicitly alongside `smbclient`:
```bash
# Debian/Ubuntu
sudo apt-get install util-linux

# Alpine Linux
apk add util-linux

# RHEL/CentOS
sudo yum install util-linux
```
This requirement does not apply when `UseSessionPool` is left at its default (`false`), or on
native Windows (UNC paths without WSL).

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

#### Using smbclient via WSL on Windows
```csharp
// Use smbclient through WSL instead of native UNC paths
builder.Services.AddSmbSharp(options =>
{
    options.UseWsl = true; // Enable WSL smbclient on Windows
    options.UseKerberos = false;
    options.Username = "username";
    options.Password = "password";
    options.Domain = "DOMAIN";
});
```

#### Persistent Session Pooling (smbclient only)
```csharp
// Reuse a small pool of authenticated smbclient sessions per share instead of paying the
// full connect/negotiate/Kerberos-or-NTLM handshake cost on every single file operation.
builder.Services.AddSmbSharp(options =>
{
    options.UseKerberos = false;
    options.Username = "username";
    options.Password = "password";
    options.Domain = "DOMAIN";
    options.UseSessionPool = true;              // opt-in, default is false
    options.SessionPoolSize = 3;                // sessions kept per (server, share), default 3
    options.SessionIdleTimeout = TimeSpan.FromMinutes(5); // evict idle sessions, default 15 minutes
});
```

### Direct Instantiation (Without Dependency Injection)

```csharp
using Microsoft.Extensions.Logging;
using SmbSharp.Business;

// Without logging
var handler = FileHandler.CreateWithKerberos();
var handler = FileHandler.CreateWithCredentials("username", "password", "DOMAIN");

// With console logging (for debugging)
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder
        .SetMinimumLevel(LogLevel.Debug)
        .AddConsole();
});

var handler = FileHandler.CreateWithKerberos(loggerFactory);
var handler = FileHandler.CreateWithCredentials("username", "password", "DOMAIN", loggerFactory);

// Using smbclient via WSL on Windows
var handler = FileHandler.CreateWithKerberos(useWsl: true);
var handler = FileHandler.CreateWithCredentials("username", "password", "DOMAIN", loggerFactory, useWsl: true);

// With persistent session pooling (reuses authenticated smbclient sessions per share)
var handler = FileHandler.CreateWithCredentials(
    "username", "password", "DOMAIN", loggerFactory,
    useWsl: false, useSessionPool: true, sessionPoolSize: 3, sessionIdleTimeout: TimeSpan.FromMinutes(5));

// Usage
var files = await handler.EnumerateFilesAsync("//server/share/folder");
```

**Note:** To use console logging, you need to add the `Microsoft.Extensions.Logging.Console` package:
```bash
dotnet add package Microsoft.Extensions.Logging.Console
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

### List Subdirectories in a Directory
```csharp
var directories = await fileHandler.EnumerateDirectoriesAsync("//server/share/folder");
foreach (var dir in directories)
{
    Console.WriteLine(dir);
}
```

### Get Extended File Metadata
```csharp
var info = await fileHandler.GetFileInfoAsync("//server/share/folder", "file.txt");
Console.WriteLine($"Created: {info.CreateTime}, Modified: {info.WriteTime}, Attributes: {info.Attributes}");
foreach (var stream in info.Streams)
{
    Console.WriteLine($"Stream: {stream}");
}
```
> **Note:** On Windows native UNC paths, `AlternateName` is null, `Streams` is empty, and `ChangeTime` falls back to
> the write time, since .NET has no built-in equivalent for smbclient's `altname`/`change_time`/alternate-data-stream
> reporting. On Linux/macOS (and Windows with WSL), this wraps smbclient's `allinfo` command and reports full details,
> including alternate data streams such as `Zone.Identifier`.

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

> **Note:** Same-share Linux/macOS (and Windows with WSL) moves use a server-side rename. Cross-share moves copy and then delete; they use create-new destination semantics. If source deletion fails, both copies may remain rather than risking data loss.

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
Credentials are passed to smbclient via the `PASSWD` child-process environment variable; the
password is not written to a credentials file or placed in command-line arguments. For WSL,
SmbSharp adds `PASSWD/u` to `WSLENV` so WSL imports the value as a Unicode environment variable.
The password is available to the smbclient process and its children while they run.
The temporary transfer directory on Windows uses the current user's Windows temp-directory ACL;
Unix mode-bit confidentiality is not guaranteed for WSL DrvFs mounts.

On the smbclient path, username/password authentication explicitly disables Kerberos
(`--use-kerberos=off`) to avoid Kerberos discovery delays before NTLM fallback. Explicit
Kerberos authentication continues to use `--use-kerberos=required`.

## Persistent Session Pooling

By default, every `IFileHandler` call on the smbclient path (Linux/macOS, or Windows via WSL)
spawns a brand-new `smbclient` process, which pays the full TCP connect + SMB negotiate +
Kerberos/NTLM handshake cost every single time. This is especially expensive with Kerberos, and
adds up quickly for frequent calls (e.g. health checks polling share connectivity).

Setting `UseSessionPool = true` (or passing `useSessionPool: true` to `FileHandler.CreateWithKerberos`/
`CreateWithCredentials`) keeps a small pool of long-lived, already-authenticated interactive
`smbclient` sessions open per `(server, share)` pair, and reuses them across calls:

- Sequential calls prefer an idle, live session. Concurrent calls use additional pool slots
  when existing sessions are busy, up to `SessionPoolSize` sessions per share.
- Each session initialization has a 30-second timeout. Broken connections and initialization
  timeouts are retried up to three total attempts with exponential backoff and jitter, starting
  at a one-second delay. Recognized authentication, permission, and missing-path errors are not
  retried. Failed initialization attempts dispose the process and leave the pool slot available
  for subsequent calls.
- Successful session establishment is logged at Information level with its duration in
  milliseconds.
- A command timeout (default two minutes) or cancellation terminates the interactive process so
  unread output cannot contaminate a later command. Commands are not automatically replayed
  after dispatch because the result of an interrupted write may be uncertain.
- Idle sessions are evicted and disposed after `SessionIdleTimeout` to avoid holding stale
  connections open indefinitely.
- This option only affects the smbclient path; on native Windows (UNC paths, no WSL), each call is
  already a direct file-system operation with no process-spawn or handshake cost, so pooling is a
  no-op there.

Under the hood, session-pooled connections invoke `smbclient` through `script` rather than directly.
On Linux, SmbSharp uses the util-linux `script -qec "<command>" /dev/null` form; on macOS it uses
the BSD `script -q /dev/null sh -c "<command>"` form. This is required because `smbclient` only prints its interactive
`smb: \>` prompt when its stdin is attached to a real TTY - a plain piped/redirected invocation
(the only kind possible from .NET's `Process` class) never produces a prompt at all, which the
persistent session relies on to know a command has finished. Wrapping with `script` allocates a
pseudo-terminal so the prompt is emitted as expected. A compatible `script` utility must be
available wherever `smbclient` runs, including inside WSL if `UseWsl` is enabled. Minimal Linux
images may ship a different implementation; pooling requires a util-linux-compatible implementation.
The macOS command form is supported but has not been validated against every OS release.

On Windows with WSL, set `WslDistribution` to select a named distro; null uses the user's default
distribution. The same selection is used for pooled and one-shot smbclient execution.

`FileHandler` instances created with `useSessionPool: true` implement `IDisposable`; dispose them
when finished to release their persistent smbclient sessions. DI-managed pools are disposed by the
service provider.

On native Windows UNC paths, cancellation is checked before blocking filesystem calls begin, but
the operating system may not interrupt a UNC call already in progress. Such a call can continue
until the underlying filesystem operation returns.

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

### Health Check Logging

When health checks fail, error logs are automatically generated to help troubleshoot connectivity issues:

```csharp
// Enable logging in your appsettings.json
{
  "Logging": {
    "LogLevel": {
      "SmbSharp.HealthChecks.SmbShareHealthCheck": "Error"
    }
  }
}
```

**Example error log:**
```
[Error] Health check failed: Unable to connect to SMB share: //server/share/folder
[Error] Health check failed for SMB share //server/share/folder: Access denied
```

## API Reference

### IFileHandler Interface

| Method | Description |
|--------|-------------|
| `EnumerateFilesAsync(directory, cancellationToken)` | Lists all files in a directory |
| `EnumerateDirectoriesAsync(directory, cancellationToken)` | Lists all subdirectories in a directory |
| `GetFileInfoAsync(directory, fileName, cancellationToken)` | Retrieves extended metadata (timestamps, attributes, alternate data streams) for a file or subdirectory |
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
    // Running on unsupported platform (not Windows/Linux/macOS)
}
```

## Debugging

To troubleshoot authentication issues or see exactly what commands are being sent to smbclient, enable debug logging:

```csharp
// In Program.cs or appsettings.json
builder.Logging.SetMinimumLevel(LogLevel.Debug);

// Or configure specific loggers
builder.Logging.AddFilter("SmbSharp.Infrastructure.ProcessWrapper", LogLevel.Debug);
```

**Example debug output:**
```
Executing process: smbclient //server/share -U "username" -c "ls folder/*"
Environment variables set: PASSWD
Process exited with code: 0
```

**Note:** Passwords are never logged. Only environment variable names (like `PASSWD`) are shown, not their values.

## Performance Considerations

### Windows
- Uses native UNC paths - very efficient
- All operations are direct file system calls
- Same-volume moves use native `File.Move` and update directory metadata without copying file contents

### Linux / macOS / Windows (WSL)
- Uses smbclient subprocess - some overhead
- Read operations download to temp file (auto-cleaned)
- Write operations upload from temp file
- Move operations = download + upload + delete (can be slow for large files)
- For large files, consider alternative approaches or be aware of 2x disk space + network transfer
- Enable `UseSessionPool` (see [Persistent Session Pooling](#persistent-session-pooling)) to avoid
  re-paying the connect/negotiate/Kerberos-or-NTLM handshake cost on every call - this is usually
  the dominant cost, especially with Kerberos or with frequent calls like health checks

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
