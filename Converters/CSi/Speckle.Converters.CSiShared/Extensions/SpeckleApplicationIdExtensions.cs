namespace Speckle.Converters.CSiShared.Extensions;

public static class SpeckleApplicationIdExtensions
{
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
  /// Retrieves the Speckle object application id for a joint object
  /// </summary>
  public static string GetSpeckleApplicationId(this CsiJointWrapper wrapper, cSapModel sapModel)
  {
    string applicationId = string.Empty;
    _ = sapModel.PointObj.GetGUID(wrapper.Name, ref applicationId);
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
}
