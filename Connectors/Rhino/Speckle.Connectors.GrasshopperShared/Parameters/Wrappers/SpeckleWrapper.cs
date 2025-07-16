using Grasshopper.Kernel.Types;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// A wrapper class for Speckle <see cref="Base"/>
/// </summary>
public abstract class SpeckleWrapper
{
  /// <summary>
  /// The name of the object. When set, this will also update the "name" property of <see cref="Base"/>.
  /// </summary>
  /// <remarks>
  /// Made virtual to allow DataObject to override with custom sync logic.
  /// </remarks>
  public virtual string Name
  {
    get => Base[Constants.NAME_PROP] as string ?? "";
    set => Base[Constants.NAME_PROP] = value;
  }

  /// <summary>
  /// The wrapped <see cref="Base"/>
  /// </summary>
  public abstract Base Base { get; set; }

  /// <summary>
  /// Represents the <see cref="Base.applicationId"/>. When set, this will also update the applicationId of <see cref="Base"/>.
  /// </summary>
  public string? ApplicationId
  {
    get => Base.applicationId;
    set => Base.applicationId = value;
  }

  /// <summary>
  /// Creates an <see cref="IGH_Goo"/> from this wrapper
  /// </summary>
  /// <returns></returns>
  public abstract IGH_Goo CreateGoo();
}
