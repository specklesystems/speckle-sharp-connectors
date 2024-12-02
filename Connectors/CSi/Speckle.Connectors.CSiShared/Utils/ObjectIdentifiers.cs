namespace Speckle.Connectors.CSiShared.Utils;

/// <summary>
/// ObjectIdentifier based on concatenating the objectType and objectName. CSi is annoying, we can't use GUIDs.
/// </summary>
/// <remarks>
/// All API methods are based on the objectType and objectName, not the GUID.
/// We will obviously manage the GUIDs but for all method calls we need a concatenated version of the objectType and objectName.
/// Since objectType is a single int (1, 2 ... 7) we know first index will always be the objectType.
/// This int gets used by the CSiWrapperFactory to create the CSiWrappers.
/// </remarks>
public static class ObjectIdentifier
{
  public static string Encode(int objectType, string objectName)
  {
    if (objectType < 1 || objectType > 7) // Both ETABS and SAP2000 APIs have the same returns for objectType
    {
      throw new ArgumentException($"Invalid object type: {objectType}. Must be between 1 and 7.");
    }
    return $"{objectType}{objectName}";
  }

  public static (int type, string name) Decode(string encodedId)
  {
    if (string.IsNullOrEmpty(encodedId) || encodedId.Length < 2) // Superfluous. But rather safe than sorry
    {
      throw new ArgumentException($"Invalid encoded ID: {encodedId}");
    }

    int objectType = int.Parse(encodedId[0].ToString());
    string objectName = encodedId[1..];

    return (objectType, objectName);
  }
}
