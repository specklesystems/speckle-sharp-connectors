using Rhino;
using Speckle.Sdk;

namespace Speckle.Importers.Rhino.Internal.FileTypeConfig;

/// <summary>
/// Represents configuration for a specific file type (e.g. <c>.skp</c>) to customise the import behaviour
/// </summary>
internal interface IFileTypeConfig : IDisposable
{
  /// <summary>
  /// Import the file at the provided <paramref name="filePath"/> into a new headless document
  /// </summary>
  /// <remarks>
  /// Implementors may apply additional cleanup and import options
  /// </remarks>
  /// <param name="filePath"></param>
  /// <returns></returns>
  /// <exception cref="SpeckleException">Rhino could not import the file</exception>
  public RhinoDoc OpenInHeadlessDocument(string filePath);
}
