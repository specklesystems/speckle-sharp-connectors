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

# Speckle 4.0 artefact rewrite

> Migrating connectors onto the client-side parquet artefact pipeline (send/receive without server-side
> serialization). Architecture, the per-connector status table, and the recipe for migrating a new connector live in
> **[`docs/4.0-artefact-rewrite.md`](docs/4.0-artefact-rewrite.md)** — read it before starting a connector. Core rule:
> every sender adds an `IArtifactRootObjectBuilder`, every receiver adds an `IArtifactHostObjectBuilder`, and the old
> `Base`-oriented logic is not used on the 4.0 path.

# Repo structure

This repo is the home of our next-generation Speckle .NET projects:

- **Desktop UI**
  - [`DUI3`](https://github.com/specklesystems/speckle-sharp-connectors/tree/main/DUI3): our next generation Desktop User Interface for all connectors.
- **Speckle Connectors**
  - [`AutoCAD Connector`](https://github.com/specklesystems/speckle-sharp-connectors/tree/main/Connectors/Autocad): for Autodesk AutoCAD and Civil3D 2023 - 2027
  - [`Rhino Connector`](https://github.com/specklesystems/speckle-sharp-connectors/tree/main/Connectors/Rhino): for McNeel Rhino 7 - 8
  - [`Revit Connector`](https://github.com/specklesystems/speckle-sharp-connectors/tree/main/Connectors/Revit): for Autodesk Revit 2023 - 2027
  - [`CSi Connector`](https://github.com/specklesystems/speckle-sharp-connectors/tree/main/Connectors/CSi): for CSi ETABS 21 - 23
  - [`Tekla Connector`](https://github.com/specklesystems/speckle-sharp-connectors/tree/main/Connectors/Tekla): for Trimble Tekla Structures 2023 - 2025
- **Speckle Converters**
  - [`AutoCAD Converter`](https://github.com/specklesystems/speckle-sharp-connectors/tree/main/Converters/Autocad)
  - [`Civil 3D Converter`](https://github.com/specklesystems/speckle-sharp-connectors/tree/main/Converters/Civil3d)
  - [`Rhino Converter`](https://github.com/specklesystems/speckle-sharp-connectors/tree/main/Converters/Rhino)
  - [`Revit Converter`](https://github.com/specklesystems/speckle-sharp-connectors/tree/main/Converters/Revit)
  - [`CSi Converter`](https://github.com/specklesystems/speckle-sharp-connectors/tree/main/Converters/CSi)
  - [`Tekla Converter`](https://github.com/specklesystems/speckle-sharp-connectors/tree/main/Converters/Tekla)
- **Importers**
    - [`Rhino`](https://github.com/specklesystems/speckle-sharp-connectors/tree/main/Importers/Rhino): Job processor and Rhino handler for file imports.
- **Common**
  - [`Connectors.Common`](https://github.com/specklesystems/speckle-sharp-connectors/tree/main/Sdk/Speckle.Connectors.Common): Common connector utilities, and dependency injection.
  - [`Connectors.Logging`](https://github.com/specklesystems/speckle-sharp-connectors/tree/main/Sdk/Speckle): OTEL.


### Other repos

Make sure to also check and ⭐️ these other Speckle next generation repositories:

- [`speckle-sharp-sdk`](https://github.com/specklesystems/speckle-sharp-sdk): our csharp SDK for next gen connectors and development
- [`speckle-sketchup`](https://github.com/specklesystems/speckle-sketchup): Sketchup connector
- [`speckle-powerbi`](https://github.com/specklesystems/speckle-powerbi): PowerBi connector
- and more [connectors & tooling](https://github.com/specklesystems/)!

# Developing and Debugging

## Developing

To build solutions in this repo, [10.0.2xx of the .NET SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) is required.

It is recommended to use Jetbrains Rider (version 2025.3 or greater) or Visual Studio 2026 (version 18.4 or greater)

From there you can open the main `Speckle.Connectors.slnx` solution and build the project.

For good development experience and environment setup, you the commands are available needed.

### Formatting
We're using [CSharpier](https://github.com/belav/csharpier) to format our code. You can use Csharpier in a few ways:
- Install CSharpier and reformat from CLI
  ```
  dotnet tool restore
  dotnet csharpier format ./
  ```
- Install the CSharpier extension for [Rider](https://plugins.jetbrains.com/plugin/18243-csharpier) or [Visual Studio](https://marketplace.visualstudio.com/items?itemName=csharpier.CSharpier)<br/>
  For best DX, we recommend turning on CSharpier's `reformat on save` setting if you've installed it in your IDE.

## Build Commands

### Clean Locks
We're using package locks to store exact and versioned dependency trees. Occasionally you will need to clean your local package-lock files, eg when switching between `Speckle.Connectors.slnx` and `Local.slnx`.
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

This is for users of the `Local.slnx` solution:

To make sure your local environment is ready for a clean build, run this command to delete all `bin` and `obj` directories and restore all projects:
```
.\build.ps1 deep-clean-local
```

## Local development with SDK changes
If you'd like to make changes to the [`speckle-sharp-sdk`](https://github.com/specklesystems/speckle-sharp-sdk) side-by-side with changes to this repo's projects, use `**Local.slnx**`. <br/>
This solution includes the Core and Objects projects from the speckle-sharp-sdk repo, and uses a new Configuration to create a build directory alongside `Debug` and `Release`.

> [!WARNING]
> Using `Local.slnx` will modify all your package locks. **Don't check these in!** Revert with the `clean-locks` command or use the regular solution to revert once your changes are made.

# Security and Licensing
      
### Security

For any security vulnerabilities or concerns, please contact us directly at security[at]speckle.systems.

### License

Unless otherwise described, the code in this repository is licensed under the Apache-2.0 License. Please note that some modules, extensions or code herein might be otherwise licensed. This is indicated either in the root of the containing folder under a different license file, or in the respective file's header. If you have any questions, don't hesitate to get in touch with us via [email](mailto:hello@speckle.systems).




