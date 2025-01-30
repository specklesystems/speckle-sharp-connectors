namespace Speckle.Converters.CSiShared.Extensions;

public static class SpeckleApplicationIdExtensions
{
  /// <summary>
  /// Retrieves the Speckle object application id for a joint object
  /// </summary>
  public static string GetSpeckleApplicationId(this CsiJointWrapper wrapper, cSapModel sapModel)
  {
    string applicationId = string.Empty;
    _ = sapModel.PointObj.GetGUID(wrapper.Name, ref applicationId);
    return applicationId;
  }

  /// <summary>
  /// Retrieves the Speckle object application id for a frame object
  /// </summary>
  public static string GetSpeckleApplicationId(this CsiFrameWrapper wrapper, cSapModel sapModel)
  {
    string applicationId = string.Empty;
    _ = sapModel.FrameObj.GetGUID(wrapper.Name, ref applicationId);
    return applicationId;
  }

  /// <summary>
  /// Retrieves the Speckle object application id for a cable object
  /// </summary>
  public static string GetSpeckleApplicationId(this CsiCableWrapper wrapper, cSapModel sapModel)
  {
    string applicationId = string.Empty;
    _ = sapModel.CableObj.GetGUID(wrapper.Name, ref applicationId);
    return applicationId;
  }

  /// <summary>
  /// Retrieves the Speckle object application id for a shell object
  /// </summary>
  public static string GetSpeckleApplicationId(this CsiShellWrapper wrapper, cSapModel sapModel)
  {
    string applicationId = string.Empty;
    _ = sapModel.AreaObj.GetGUID(wrapper.Name, ref applicationId);
    return applicationId;
  }

  /// <summary>
  /// Retrieves the Speckle object application id for a solid object
  /// </summary>
  public static string GetSpeckleApplicationId(this CsiSolidWrapper wrapper, cSapModel sapModel)
  {
    string applicationId = string.Empty;
    _ = sapModel.SolidObj.GetGUID(wrapper.Name, ref applicationId);
    return applicationId;
  }

  /// <summary>
  /// Retrieves the Speckle object application id for a link object
  /// </summary>
  public static string GetSpeckleApplicationId(this CsiLinkWrapper wrapper, cSapModel sapModel)
  {
    string applicationId = string.Empty;
    _ = sapModel.LinkObj.GetGUID(wrapper.Name, ref applicationId);
    return applicationId;
  }
}
