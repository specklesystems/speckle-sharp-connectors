namespace Build;

public static class Consts
{
  public static readonly string[] Solutions = ["Speckle.Connectors.sln"];

  public static readonly ProjectGroup[] ProjectGroups =
  {
    new("arcgis", [new("Connectors/ArcGIS/Speckle.Connectors.ArcGIS3", "net6.0-windows")]),
    new(
      "rhino",
      [
        new("Connectors/Rhino/Speckle.Connectors.Rhino7", "net48"),
        new("Connectors/Rhino/Speckle.Connectors.Rhino8", "net48"),
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
    new(
      "autocad",
      [
        new("Connectors/Autocad/Speckle.Connectors.Autocad2022", "net48"),
        new("Connectors/Autocad/Speckle.Connectors.Autocad2023", "net48"),
        new("Connectors/Autocad/Speckle.Connectors.Autocad2024", "net48"),
        new("Connectors/Autocad/Speckle.Connectors.Autocad2025", "net8.0-windows"),
        new("Connectors/Autocad/Speckle.Connectors.Autocad2026", "net8.0-windows")
      ]
    ),
    new(
      "civil3d",
      [
        new("Connectors/Autocad/Speckle.Connectors.Civil3d2022", "net48"),
        new("Connectors/Autocad/Speckle.Connectors.Civil3d2023", "net48"),
        new("Connectors/Autocad/Speckle.Connectors.Civil3d2024", "net48"),
        new("Connectors/Autocad/Speckle.Connectors.Civil3d2025", "net8.0-windows"),
        new("Connectors/Autocad/Speckle.Connectors.Civil3d2026", "net8.0-windows")
      ]
    ),
    new(
      "navisworks",
      [
        new("Connectors/Navisworks/Speckle.Connectors.Navisworks2020", "net48"),
        new("Connectors/Navisworks/Speckle.Connectors.Navisworks2021", "net48"),
        new("Connectors/Navisworks/Speckle.Connectors.Navisworks2022", "net48"),
        new("Connectors/Navisworks/Speckle.Connectors.Navisworks2023", "net48"),
        new("Connectors/Navisworks/Speckle.Connectors.Navisworks2024", "net48"),
        new("Connectors/Navisworks/Speckle.Connectors.Navisworks2025", "net48"),
        new("Connectors/Navisworks/Speckle.Connectors.Navisworks2026", "net48")
      ]
    ),
    new(
      "teklastructures",
      [
        new("Connectors/Tekla/Speckle.Connector.Tekla2023", "net48"),
        new("Connectors/Tekla/Speckle.Connector.Tekla2024", "net48")
      ]
    ),
    new(
      "etabs",
      [
        new("Connectors/CSi/Speckle.Connectors.ETABS21", "net48"),
        new("Connectors/CSi/Speckle.Connectors.ETABS22", "net8.0-windows"),
      ]
    )
  };
}

public readonly record struct ProjectGroup(string HostAppSlug, IReadOnlyList<InstallerAsset> Projects)
{
  public override string ToString() => $"{HostAppSlug}";
}

public readonly record struct InstallerAsset(string ProjectPath, string TargetName);
