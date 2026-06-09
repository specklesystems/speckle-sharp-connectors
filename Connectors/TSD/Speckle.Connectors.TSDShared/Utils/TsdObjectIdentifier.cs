using TSD.API.Remoting.Common;

namespace Speckle.Connectors.TSDShared.Utils;

internal static class TsdObjectIdentifier
{
  public static string Encode(EntityType entityType, int index) => $"{(int)entityType}:{index}";

  public static (EntityType type, int index) Decode(string encodedId)
  {
    if (string.IsNullOrEmpty(encodedId))
    {
      throw new ArgumentException($"Invalid encoded ID: {encodedId}");
    }

    var parts = encodedId.Split(':');
    if (parts.Length != 2 || !int.TryParse(parts[0], out int typeValue) || !int.TryParse(parts[1], out int index))
    {
      throw new ArgumentException($"Invalid encoded ID: {encodedId}");
    }

    return ((EntityType)typeValue, index);
  }
}
