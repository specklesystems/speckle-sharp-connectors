using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.DUI.Settings;

namespace Speckle.Connectors.Autocad.Operations.Receive;

public sealed class ReceivePreferPolyFaceMeshSetting(bool value = ReceivePreferPolyFaceMeshSetting.DEFAULT_VALUE)
  : ICardSetting
{
  public const string SETTING_ID = "preferPolyFaceMesh";

  // On, so a model card saved without this setting keeps receiving meshes the way it always has.
  public const bool DEFAULT_VALUE = true;

  public string? Id { get; set; } = SETTING_ID;
  public string? Title { get; set; } = "Prefer PolyFaceMesh over SubDMesh";
  public string? Description { get; set; } =
    "Receives meshes as PolyFaceMesh, using SubDMesh only for meshes with more than 32,767 vertices. Turn off to receive every mesh as a SubDMesh.";
  public string? Type { get; set; } = "boolean";
  public object? Value { get; set; } = value;
  public List<string>? Enum { get; set; }
}

public static class ReceiveMeshSettings
{
  public static bool GetPreferPolyFaceMesh(ModelCard card) =>
    card.Settings?.FirstOrDefault(x => x.Id == ReceivePreferPolyFaceMeshSetting.SETTING_ID)?.Value as bool?
    ?? ReceivePreferPolyFaceMeshSetting.DEFAULT_VALUE;
}
