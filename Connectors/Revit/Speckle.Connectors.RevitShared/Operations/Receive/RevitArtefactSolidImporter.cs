using System.IO;
using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Diagnostics;
using Speckle.Converters.Common;
using Speckle.Converters.Common.FileOps;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk;
using Speckle.Sdk.Pipelines.Receive.Artifacts;
// aliased, not imported: Speckle.Objects.Other.Transform would collide with DB.Transform
using RawEncodingFormats = Speckle.Objects.Other.RawEncodingFormats;

namespace Speckle.Connectors.Revit.Operations.Receive;

/// <summary>
/// Turns the raw 3dm <c>SOLID</c> blobs of a Speckle 4.0 artefact bundle into real Revit solids, and paints the
/// elements baked from them (an imported solid carries no material of its own, unlike a tessellated mesh which bakes
/// one into every <see cref="TessellatedFace"/>). Split out of <see cref="RevitHostObjectArtefactBuilder"/> so the
/// Revit-API surface the raw import needs — <see cref="ShapeImporter"/>, <c>SubTransaction</c>,
/// <see cref="SolidUtils"/>, painting — stays out of the builder's own coupling budget.
/// </summary>
/// <remarks>
/// Drives the same machinery as the v1 <c>IRawEncodedObjectConverter</c> on the <c>Base</c>-graph receive path, and
/// deliberately keeps its semantics [ENG-8800]: the import runs in a <c>SubTransaction</c> that is rolled back
/// when it yields nothing, because a failed <see cref="ShapeImporter"/> leaves "ghost" state behind that breaks later
/// painting and group creation.
/// </remarks>
public sealed class RevitArtefactSolidImporter
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly ILogger<RevitArtefactSolidImporter> _logger;

  public RevitArtefactSolidImporter(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    ILogger<RevitArtefactSolidImporter> logger
  )
  {
    _converterSettings = converterSettings;
    _logger = logger;
  }

  /// <summary>
  /// Imports the given geometry indices as Revit solids. Anything that isn't <c>RHINO_3DM</c> is filtered out up front:
  /// a foreign blob (e.g. AutoCAD's SAT) must never reach the importer. An empty result is the caller's signal to fall
  /// back to the object's DISPLAY meshes — which happens when the importer rejects the blob outright or degrades it to
  /// <see cref="Mesh"/>, since its meshes are worse than ours (they can't be painted and carry no per-face material).
  /// </summary>
  public List<GeometryObject> ImportSolids(
    Document doc,
    ArtefactBundle bundle,
    IReadOnlyList<int> solidKs,
    ArtefactSessionLog session
  )
  {
    var result = new List<GeometryObject>();
    foreach (var solidK in solidKs)
    {
      if (
        !bundle.Geometries.TryGetValue(solidK, out var g)
        || g.Type != RawEncodingFormats.RHINO_3DM
        || g.Content.Length == 0
      )
      {
        continue;
      }
      try
      {
        var imported = ImportShape(doc, g.Content);
        if (imported.Count == 0)
        {
          session.Increment("solidImportEmpty");
          continue;
        }
        if (imported.Any(o => o is Mesh))
        {
          session.Increment("solidImportDegradedToMesh");
          continue;
        }
        // Solids come back in the source file's own coordinates — unlike mesh vertices (ToInternalPoints), nothing has
        // re-based them, so apply the composed reference-point transform here [ENG-8808/ENG-8947]. A non-solid the
        // transform can't be applied to counts as a failed import, so the caller uses the re-based display meshes.
        if (_converterSettings.Current.ReferencePointTransform is Transform transform)
        {
          if (imported.Any(o => o is not Solid))
          {
            session.Increment("solidImportNotTransformable");
            continue;
          }
          imported = imported.Select(o => (GeometryObject)SolidUtils.CreateTransformed((Solid)o, transform)).ToList();
        }
        result.AddRange(imported);
        session.Increment("solidsImported");
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        session.Increment("solidImportFailed");
        _logger.LogWarning(
          ex,
          "3dm solid import failed for geometry {GeomK} ({Bytes} bytes)",
          solidK,
          g.Content.Length
        );
      }
    }
    return result;
  }

  /// <summary>Pulls the solids out of a decoded geometry list, leaving the rest (meshes) in place.</summary>
  public List<GeometryObject> ExtractSolids(List<GeometryObject> geometry)
  {
    var solids = geometry.OfType<Solid>().Cast<GeometryObject>().ToList();
    geometry.RemoveAll(o => o is Solid);
    return solids;
  }

  /// <summary>Copies definition-space solids into one placement's coordinates — see <c>BuiltDefinition</c>.</summary>
  public List<GeometryObject> TransformSolids(IReadOnlyList<GeometryObject> solids, Transform transform) =>
    solids.OfType<Solid>().Select(s => (GeometryObject)SolidUtils.CreateTransformed(s, transform)).ToList();

  private static List<GeometryObject> ImportShape(Document doc, byte[] content)
  {
    var filePath = TempFileProvider.GetTempFile("RevitArtefact", RawEncodingFormats.RHINO_3DM);
    File.WriteAllBytes(filePath, content);

    IList<GeometryObject> imported;
    using (var subTx = new SubTransaction(doc))
    {
      subTx.Start();
      using var importer = new ShapeImporter();
      imported = importer.Convert(doc, filePath);
      if (imported.Count == 0)
      {
        subTx.RollBack(); // clean up the invalid state Revit created, otherwise we get "ghost" objects
      }
      else
      {
        subTx.Commit();
      }
    }
    return imported.ToList();
  }

  /// <summary>
  /// Paints every face of the elements baked from imported solids with their object's material — the same approach as
  /// v1 <c>RevitHostObjectBuilder.PostBakePaint</c>, and like it this must run in its own transaction <b>after</b> the
  /// bake committed, since an element's faces aren't queryable before then. Per element best-effort: painting is
  /// cosmetic, so an element Revit won't paint (a placement whose solids live inside a <see cref="GeometryInstance"/>
  /// being the likely case) keeps its geometry and is counted in the session log instead of failing the object.
  /// </summary>
  public void PaintSolids(
    Document doc,
    List<(ElementId Element, ElementId Material)> paintTargets,
    ArtefactSessionLog session
  )
  {
    foreach (var (elementId, materialId) in paintTargets)
    {
      try
      {
        if (doc.GetElement(elementId) is not Element element)
        {
          continue;
        }
        int painted = 0;
        foreach (var geo in element.get_Geometry(new Options() { DetailLevel = ViewDetailLevel.Undefined }))
        {
          switch (geo)
          {
            case Solid solid:
              painted += PaintFaces(doc, elementId, solid, materialId);
              break;
            // a placement's geometry is a GeometryInstance over the shared definition
            case GeometryInstance instance:
              foreach (var nested in instance.GetInstanceGeometry())
              {
                if (nested is Solid nestedSolid)
                {
                  painted += PaintFaces(doc, elementId, nestedSolid, materialId);
                }
              }
              break;
            default:
              break;
          }
        }
        session.Increment(painted > 0 ? "solidsPainted" : "solidsUnpainted");
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        session.Increment("solidPaintFailed");
        _logger.LogDebug(ex, "Could not paint imported solid faces on element {ElementId}", elementId);
      }
    }
  }

  private static int PaintFaces(Document doc, ElementId elementId, Solid solid, ElementId materialId)
  {
    int painted = 0;
    foreach (Face face in solid.Faces)
    {
      doc.Paint(elementId, face, materialId);
      painted++;
    }
    return painted;
  }
}
