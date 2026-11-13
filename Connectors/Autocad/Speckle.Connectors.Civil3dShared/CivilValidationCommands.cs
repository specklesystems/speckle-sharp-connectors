#if DEBUG
using System.Globalization;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Speckle.Converters.Civil3dShared.Helpers;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using CDB = Autodesk.Civil.DatabaseServices;

namespace Speckle.Connectors.Civil3dShared;

/// <summary>One object and one stage per invocation. No drawing writes or bulk retries.</summary>
public class CivilValidationCommands
{
  [CommandMethod("BUILDCIVILCHECK", CommandFlags.Modal)]
  [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Diagnostic stops on its first managed failure and records it; native faults are not recoverable here.")]
  public void Report()
  {
    var doc = AcApp.DocumentManager.MdiActiveDocument;
    var selected = doc.Editor.GetString("\nOne civil object handle (hex): ");
    if (selected.Status != PromptStatus.OK || !long.TryParse(selected.StringResult, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long handle))
    {
      return;
    }
    var stage = doc.Editor.GetKeywords(new PromptKeywordOptions("\nStage [Definition/Geometry]", "Definition Geometry"));
    if (stage.Status != PromptStatus.OK)
    {
      return;
    }
    string directory = Path.GetDirectoryName(doc.Name) ?? Path.GetTempPath();
    string path = Path.Combine(directory, $"civil-probe-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{handle:X}-{stage.StringResult}-{Guid.NewGuid():N}.jsonl");
    using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
    using var writer = new StreamWriter(output) { AutoFlush = true };
    void Trace(string field) => writer.WriteLine(JsonSerializer.Serialize(new { phase = field }));
    writer.WriteLine(JsonSerializer.Serialize(new { drawing = doc.Name, handle = selected.StringResult, stage = stage.StringResult }));
    try
    {
      using var tr = doc.Database.TransactionManager.StartOpenCloseTransaction();
      Trace("Database.GetObjectId");
      var id = doc.Database.GetObjectId(false, new Handle(handle), 0);
      Trace("Transaction.GetObject.ForRead");
      if (tr.GetObject(id, OpenMode.ForRead) is not CDB.Entity entity)
      {
        throw new InvalidOperationException("Selected object is not a Civil entity.");
      }
      if (stage.StringResult == "Definition")
      {
        var definition = CivilDefinitionExtractor.Extract(entity, Trace);
        writer.WriteLine(JsonSerializer.Serialize(new { type = entity.GetType().Name, definition }));
      }
      else
      {
        Trace(entity.GetType().Name + ".Geometry");
        double[][][] lines = entity switch
        {
          CDB.Profile p => new[] { CivilLinearGeometry.ProfilePoints(p).Select(v => new[] { v.X, v.Y, v.Z }).ToArray() },
          CDB.Section s => CivilLinearGeometry.SectionSegments(s).Select(segment => segment.Select(v => new[] { v.X, v.Y, v.Z }).ToArray()).ToArray(),
          CDB.SampleLine s => new[] { s.Vertices.Cast<CDB.SampleLineVertex>().Select(v => new[] { v.Location.X, v.Location.Y, v.Location.Z }).ToArray() },
          _ => throw new NotSupportedException("Isolated geometry stage supports Profile, Section and SampleLine only."),
        };
        writer.WriteLine(JsonSerializer.Serialize(new
        {
          type = entity.GetType().Name,
          coordinateSpace = entity is CDB.Section ? "section-offset-elevation" : "drawing-wcs",
          lines,
        }));
      }
      writer.WriteLine(JsonSerializer.Serialize(new { complete = true }));
      doc.Editor.WriteMessage($"\nPASS: single-object report saved: {path}\n");
    }
    catch (System.Exception ex)
    {
      writer.WriteLine(JsonSerializer.Serialize(new { complete = false, error = ex.ToString() }));
      doc.Editor.WriteMessage($"\nSTOPPED at first error: {ex.Message}\nReport: {path}\n");
    }
  }
}
#endif
