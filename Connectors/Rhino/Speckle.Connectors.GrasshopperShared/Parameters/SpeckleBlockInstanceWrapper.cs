using Speckle.Sdk.Models;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// Wrapper around a block instance.
/// </summary>
public class SpeckleBlockInstanceWrapper : SpeckleWrapper
{
  public override required Base Base { get; set; }
}
