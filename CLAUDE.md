# SmbSharp – Claude Instructions

## Before Any Push (Git or NuGet)

Always run the full test suite and confirm all tests pass before pushing:

```bash
dotnet test
```

Never push to Git or NuGet if any tests are failing.

## Publishing to NuGet

Always do a **clean build** before packing to avoid stale binaries ending up in the package:

```bash
dotnet clean SmbSharp/SmbSharp.csproj -c Release
dotnet build SmbSharp/SmbSharp.csproj -c Release
dotnet pack SmbSharp/SmbSharp.csproj -c Release --no-build -o ./nupkg
dotnet nuget push ./nupkg/SmbSharp.<version>.nupkg --api-key $NUGET_API_KEY --source https://api.nuget.org/v3/index.json
```

Never use `dotnet pack` alone without a preceding clean + build — it can reuse cached binaries from a previous build and produce a package with outdated DLLs.
