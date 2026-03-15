# Building and Deploying Email.Smtp

This guide explains how to build, pack, and publish this NuGet package.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- Git
- A [NuGet.org](https://www.nuget.org/) account (for publishing)

## Building the Package

### Clone the Repository

```bash
git clone https://github.com/openmindednewby/Email.Smtp.git
cd Email.Smtp
```

### Restore Dependencies

```bash
dotnet restore
```

### Build

```bash
# Debug build
dotnet build

# Release build
dotnet build -c Release
```

## Creating a NuGet Package

### Pack the Package

```bash
dotnet pack -c Release -o ./artifacts
```

This creates a `.nupkg` file in the `./artifacts` folder.

## Publishing to NuGet.org

### Using publish.ps1 (Recommended)

```powershell
.\publish.ps1 -Bump patch -ApiKey YOUR_API_KEY
```

The script auto-bumps the version, builds, packs, and pushes. On failure it rolls back the version change.

### Manual Publish

```bash
dotnet nuget push ./artifacts/Email.Smtp.1.0.0.nupkg \
  --api-key YOUR_API_KEY \
  --source https://api.nuget.org/v3/index.json
```

## Version Management

This package follows [Semantic Versioning](https://semver.org/):

- **Patch** (1.0.0 -> 1.0.1): Bug fixes, no breaking changes
- **Minor** (1.0.0 -> 1.1.0): New features, backward compatible
- **Major** (1.0.0 -> 2.0.0): Breaking changes

## Resources

- [NuGet Package Page](https://www.nuget.org/packages/Email.Smtp)
- [GitHub Repository](https://github.com/openmindednewby/Email.Smtp)
