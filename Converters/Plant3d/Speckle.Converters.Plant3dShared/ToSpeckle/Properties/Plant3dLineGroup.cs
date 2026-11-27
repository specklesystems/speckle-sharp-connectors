namespace Speckle.Converters.Plant3dShared.ToSpeckle;

/// <summary>
/// Data-only shape of a Plant3D line group as stored in the "Data Manager" properties.
/// Written by the converter and read by the connector via <see cref="Plant3dDataManagerProperties"/>.
/// </summary>
public sealed record Plant3dLineGroup(
  int Id,
  string Type,
  string LineNumber,
  string? Tag,
  string? NominalSize,
  string? NominalSpec
);
