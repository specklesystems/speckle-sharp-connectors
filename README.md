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

First build the SDK repo to create nugets:
![image](/Images/pack.png)

Then build the connectors repo to create a local nuget feed with a custom command:
![image](/Images/add-local-sdk.png)

Then change your local connectors `Directory.Packages.props`

![image](/Images/update-local.png)

A restore from your IDE will modify package locks and use the new feed for a new version instead of using nuget because the version shouldn't exist yet

## Other Build commands

### Clean Locks

Run this to delete package.lock.json files when restores go run.

![image](/Images/clean-locks.png)