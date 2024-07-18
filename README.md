# Speckle 3.0 Connectors!
[![codecov](https://codecov.io/gh/specklesystems/speckle-sharp-connectors/graph/badge.svg?token=eMhI4M8umi)](https://codecov.io/gh/specklesystems/speckle-sharp-connectors)

## Formatting

Use [CSharpier](https://github.com/belav/csharpier) to format.  There are a few options:
- Install CSharpier as a local tool: `dotnet tool install csharpier`
    - This allows CLI use of CSharpier: `dotnet csharpier .` after `dotnet tool restore`
- Install the CSharpier Visual Studio 2022 extension: https://marketplace.visualstudio.com/items?itemName=csharpier.CSharpier
- Install CSharpier as a global tool: `dotnet tool install csharpier -g`
    - This allows CLI use of CSharpier: `dotnet csharpier .` after `dotnet tool restore`

## Local development with SDK changes

Use the `Local.sln` to include the Core and Objects projects from the SDK repo.  This setup assumes the two Git repos are side by side.

Using the `Local.sln` will modify all your package locks.  Don't check these in!  Revert or use the regular solution to revert once your changes are made.

## Other Build commands

### Clean Locks

Run this to delete package.lock.json files when restores go run.

![image](/Images/clean-locks.png)