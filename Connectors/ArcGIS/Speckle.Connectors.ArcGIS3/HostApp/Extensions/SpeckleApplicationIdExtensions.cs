namespace Speckle.Connectors.ArcGIS.HostApp.Extensions;

public static class SpeckleApplicationIdExtensions
{
  /// <summary>
  /// Retrieves the Speckle application id for map members
  /// </summary>
  public static string GetSpeckleApplicationId(this ADM.MapMember mapMember) => mapMember.URI;
}
