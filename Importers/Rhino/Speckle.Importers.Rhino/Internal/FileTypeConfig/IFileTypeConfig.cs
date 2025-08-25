using Rhino;
using Rhino.Collections;

namespace Speckle.Importers.Rhino.Internal.FileTypeConfig;

/// <summary>
/// Represents configuration for a specific file type (e.g. <c>.skp</c>) to customise the import behaviour
/// </summary>
internal interface IFileTypeConfig
{
  /// <summary>
  /// Options to pass to the <see cref="RhinoDoc.Import(string, ArchivableDictionary?)"/> command
  /// </summary>
  public ArchivableDictionary? ImportOptions { get; }

  /// <summary>
  /// Run any operations on objects in the rhino document to clean up the export before converting to speckle
  /// </summary>
  /// <remarks>
  /// Ran on the document after importing, but before any Speckle conversion
  /// </remarks>
  /// <param name="doc"></param>
  public void PreProcessDocument(RhinoDoc doc);
}
