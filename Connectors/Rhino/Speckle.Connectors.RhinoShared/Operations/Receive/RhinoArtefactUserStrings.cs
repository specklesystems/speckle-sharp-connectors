using Rhino.DocObjects;
using Speckle.Connectors.Common.Conversion;
using Speckle.Sdk.Pipelines.Receive.Artifacts;

namespace Speckle.Connectors.Rhino.Operations.Receive;

/// <summary>
/// Writes an artefact object's eav properties onto Rhino object attributes as user strings, so Rhino user text,
/// nested user dictionaries and foreign parameters (Revit/IFC) survive an artefact receive [ENG-9111].
/// </summary>
/// <remarks>
/// The key shape is the legacy one (<c>SpeckleAttributeExtensions.GetAttributes</c>): nested dictionaries flatten to
/// dot-delimited paths, so <c>{ "User Dictionary": { "phase": 2 } }</c> becomes <c>User Dictionary.phase</c>. The
/// bundle's paths are dotted already, so nothing needs flattening; which keys are user data and how a value is
/// formatted is the stack-wide rule in <see cref="ArtefactUserProperties"/>.
/// </remarks>
internal static class RhinoArtefactUserStrings
{
  public static void Apply(ObjectAttributes atts, PropertyView properties)
  {
    foreach (var kvp in ArtefactUserProperties.Filter(properties))
    {
      atts.SetUserString(kvp.Key, ArtefactUserProperties.Stringify(kvp.Value));
    }
  }
}
