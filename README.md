<h1 align="center">
  <img src="https://user-images.githubusercontent.com/2679513/131189167-18ea5fe1-c578-47f6-9785-3748178e4312.png" width="150px"/><br/>
  Speckle | Sharp | Connectors
</h1>

<p align="center"><a href="https://twitter.com/SpeckleSystems"><img src="https://img.shields.io/twitter/follow/SpeckleSystems?style=social" alt="Twitter Follow"></a> <a href="https://speckle.community"><img src="https://img.shields.io/discourse/users?server=https%3A%2F%2Fspeckle.community&amp;style=flat-square&amp;logo=discourse&amp;logoColor=white" alt="Community forum users"></a> <a href="https://speckle.systems"><img src="https://img.shields.io/badge/https://-speckle.systems-royalblue?style=flat-square" alt="website"></a> <a href="https://speckle.guide/dev/"><img src="https://img.shields.io/badge/docs-speckle.guide-orange?style=flat-square&amp;logo=read-the-docs&amp;logoColor=white" alt="docs"></a></p>

> Speckle is the first AEC data hub that connects with your favorite AEC tools. Speckle exists to overcome the challenges of working in a fragmented industry where communication, creative workflows, and the exchange of data are often hindered by siloed software and processes. It is here to make the industry better.

<h3 align="center">
    .NET Desktop UI, Connectors, and Converters
</h3>

<p align="center"><a href="https://codecov.io/gh/specklesystems/speckle-sharp-connectors"><img src="https://codecov.io/gh/specklesystems/speckle-sharp-connectors/graph/badge.svg?token=eMhI4M8umi" alt="Codecov"></a></p>

# Repo structure

This repo is the home of our next-generation Speckle .NET projects:

- **Desktop UI**
  - [`DUI3`](https://github.com/specklesystems/speckle-sharp-connectors/tree/main/DUI3): our next generation Desktop User Interface for all connectors.
- **Speckle Connectors**
  - [`Autocad Connector`](https://github.com/specklesystems/speckle-sharp-connectors/tree/main/Connectors/Autocad): for Autodesk AutoCAD and Civil3D 2022+
  - [`Rhino Connector`](https://github.com/specklesystems/speckle-sharp-connectors/tree/main/Connectors/Rhino): for McNeel Rhino 7+
  - [`Revit Connector`](https://github.com/specklesystems/speckle-sharp-connectors/tree/main/Connectors/Revit): for Autodesk Revit 2022+
  - [`ArcGIS Connector`](https://github.com/specklesystems/speckle-sharp-connectors/tree/main/Connectors/ArcGIS/Speckle.Connectors.ArcGIS3): for Esri ArcGIS
  - [`Tekla Connector`](https://github.com/specklesystems/speckle-sharp-connectors/tree/main/Connectors/Tekla): for Trimble Tekla 2024
- **Speckle Converters**
  - [`Autocad Converter`](https://github.com/specklesystems/speckle-sharp-connectors/tree/main/Converters/Autocad): for Autodesk AutoCAD 2022+
  - [`Civil3d Converter`](https://github.com/specklesystems/speckle-sharp-connectors/tree/main/Converters/Civil3d): for Autodesk Civil3D 2022+
  - [`Rhino Converter`](https://github.com/specklesystems/speckle-sharp-connectors/tree/main/Converters/Rhino): for McNeel Rhino 7+
  - [`Revit Converter`](https://github.com/specklesystems/speckle-sharp-connectors/tree/main/Converters/Revit): for Autodesk Revit 2023+
  - [`ArcGIS Converter`](https://github.com/specklesystems/speckle-sharp-connectors/tree/main/Converters/ArcGIS/Speckle.Converters.ArcGIS3): for Esri ArcGIS
  - [`Tekla Converter`](https://github.com/specklesystems/speckle-sharp-connectors/tree/main/Converters/Tekla/Speckle.Converter.Tekla2024): for Trimble Tekla 2024
- **SDK**
  - [`SDK`](https://github.com/specklesystems/speckle-sharp-connectors/tree/main/Sdk): Autofac module, connector utilities, and dependency injection.


### Other repos

Make sure to also check and ⭐️ these other Speckle next generation repositories:

- [`speckle-sharp-sdk`](https://github.com/specklesystems/speckle-sharp-sdk): our csharp SDK for next gen connectors and development
- [`speckle-sketchup`](https://github.com/specklesystems/speckle-sketchup): Sketchup connector
- [`speckle-powerbi`](https://github.com/specklesystems/speckle-powerbi): PowerBi connector
- and more [connectors & tooling](https://github.com/specklesystems/)!

# Developing and Debugging

Clone this repo. **Each section has its own readme**, so follow each readme for specific build and debug instructions.

Issues or questions? We encourage everyone interested to debug / hack / contribute / give feedback to this project.

> **A note on Accounts:**
> The connectors themselves don't have features to manage your Speckle accounts; this functionality is delegated to the Speckle Manager desktop app. You can install it [from here](https://speckle-releases.ams3.digitaloceanspaces.com/manager/SpeckleManager%20Setup.exe).

## Local Builds

For good development experience and environment setup, run the commands below as needed.

### Switching to SLNX

SLNX was introduced with .NET 9 (in May 2024), Visual Studio 17.13 and Rider 2024.3.  The older SLNs being used remain for now but will be removed when .NET 10 is introduced to the repo.  SLNXs specific to certain host apps are being generated from the main SLN to allow for faster developmenet.

[https://devblogs.microsoft.com/dotnet/introducing-slnx-support-dotnet-cli/](https://devblogs.microsoft.com/dotnet/introducing-slnx-support-dotnet-cli/)

[https://devblogs.microsoft.com/visualstudio/new-simpler-solution-file-format/](https://devblogs.microsoft.com/visualstudio/new-simpler-solution-file-format/)

### Formatting
We're using [CSharpier](https://github.com/belav/csharpier) to format our code.  You can install Csharpier in a few ways:
- Install CSharpier as a local tool and reformat from CLI
  ```
  dotnet tool install csharpier
  dotnet csharpier
  ```
- Install CSharpier as a global tool and reformat from CLI
  ```
  dotnet tool install csharpier -g
  dotnet csharpier
  ```
- Install the CSharpier extension for Visual Studio or Rider.<br/>
  For best DX, we recommend turning on CSharpier's `reformat on save` setting if you've installed it in your IDE.

### Clean Locks
We're using npm package locks to store exact and versioned dependency trees. Occasionally you will need to clean your local package-lock files, eg when switching between `Speckle.Connectors.sln` and `Local.sln`.
Run this command in CLI to delete all package.lock.json files before a restore:
```
.\build.ps1 clean-locks
```

### Deep Clean
To make sure your local environment is ready for a clean build, run this command to delete all `bin` and `obj` directories and restore all projects:
```
.\build.ps1 deep-clean
```
### Deep Clean Local

This is for users of the `Local.sln` solution:

To make sure your local environment is ready for a clean build, run this command to delete all `bin` and `obj` directories and restore all projects:
```
.\build.ps1 deep-clean-local
```

## Local development with SDK changes
If you'd like to make changes to the [`speckle-sharp-sdk`](https://github.com/specklesystems/speckle-sharp-sdk) side-by-side with changes to this repo's projects, use `**Local.sln**`. <br/>
This solution includes the Core and Objects projects from the speckle-sharp-sdk repo, and uses a new Configuration to create a build directory alongside `Debug` and `Release`.

> [!WARNING]
> Using `Local.sln` will modify all your package locks. **Don't check these in!** Revert with the `clean-locks` command or use the regular solution to revert once your changes are made.

# Security and Licensing
      
### Security

For any security vulnerabilities or concerns, please contact us directly at security[at]speckle.systems.

### License

Unless otherwise described, the code in this repository is licensed under the Apache-2.0 License. Please note that some modules, extensions or code herein might be otherwise licensed. This is indicated either in the root of the containing folder under a different license file, or in the respective file's header. If you have any questions, don't hesitate to get in touch with us via [email](mailto:hello@speckle.systems).




