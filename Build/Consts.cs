namespace Build;

public static class Consts
{
  public static readonly string[] Solutions = ["Speckle.Connectors.sln"];

  public static readonly InstallerProject[] InstallerManifests =
  {
    new("arcgis", [new("Connectors/ArcGIS/Speckle.Connectors.ArcGIS3", "net6.0-windows")]),
    new(
      "rhino",
      [
        new("Connectors/Rhino/Speckle.Connectors.Rhino7", "net48"),
        new("Connectors/Rhino/Speckle.Connectors.Rhino8", "net48")
      ]
    ),
    new(
      "revit",
      [
        new("Connectors/Revit/Speckle.Connectors.Revit2022", "net48"),
        new("Connectors/Revit/Speckle.Connectors.Revit2023", "net48"),
        new("Connectors/Revit/Speckle.Connectors.Revit2024", "net48"),
        new("Connectors/Revit/Speckle.Connectors.Revit2025", "net8.0-windows")
      ]
    ),
    new("autocad", [new("Connectors/Autocad/Speckle.Connectors.Autocad2023", "net48")])
  };
}

public readonly record struct InstallerProject(string HostAppSlug, IReadOnlyList<InstallerAsset> Projects)
{
  public override string ToString() => $"{HostAppSlug}";
}

public readonly record struct InstallerAsset(string ProjectPath, string TargetName);
