using Speckle.Sdk.Models;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public abstract class SpeckleWrapper
{
  public abstract required Base Base { get; set; }

  private string? _applicationId;

  /// <summary>
  /// Represents the <see cref="Base.applicationId"/>. When set, this will also update the applicationId of <see cref="Base"/>.
  /// </summary>
  public string? ApplicationId
  {
    get => _applicationId;
    set
    {
      _applicationId = value;
      Base.applicationId = value;
    }
  }

  /// <summary>
  /// The color of the <see cref="Base"/>
  /// </summary>
  public required Color? Color { get; set; }

  /// <summary>
  /// The material of the <see cref="Base"/>
  /// </summary>
  public required SpeckleMaterialWrapper? Material { get; set; }

  /// <summary>
  /// Represents the guid of this <see cref="SpeckleWrapper"/>
  /// </summary>
  /// <remarks>This property will usually be assigned in create components, or in publish components</remarks>
  public required string? WrapperGuid { get; set; }
}
