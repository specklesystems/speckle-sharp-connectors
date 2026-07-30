using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.Geometry;

[NameAndRankValue(typeof(SOG.Mesh), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class MeshToHostConverter : IToHostTopLevelConverter, ITypedConverter<SOG.Mesh, ADB.Entity>
{
  /// <summary>
  /// A <see cref="ADB.PolyFaceMesh"/> addresses its vertices through <see cref="ADB.FaceRecord"/>, whose 1-based
  /// vertex indices are 16-bit — so it cannot hold more than <see cref="short.MaxValue"/> vertices. Beyond that the
  /// index casts silently wrapped negative and the mesh came in as garbage [ENG-8836]. Larger meshes are built as a
  /// native MESH (<see cref="ADB.SubDMesh"/>) instead, whose face array is 32-bit.
  /// </summary>
  private const int MAX_POLYFACE_MESH_VERTICES = short.MaxValue;

  private readonly ITypedConverter<SOG.Point, AG.Point3d> _pointConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public MeshToHostConverter(
    ITypedConverter<SOG.Point, AG.Point3d> pointConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _settingsStore = settingsStore;
  }

  public object Convert(Base target) => Convert((SOG.Mesh)target);

  /// <remarks>
  /// Mesh conversion requires transaction since it's vertices needed to be added into database in advance..
  /// </remarks>
  public ADB.Entity Convert(SOG.Mesh target)
  {
    target.TriangulateMesh(true);

    // get vertex points
    List<AG.Point3d> points = target.GetPoints().Select(o => _pointConverter.Convert(o)).ToList();

    //TODO using?
    ADB.Transaction tr = _settingsStore.Current.Document.TransactionManager.TopTransaction;

    // both mesh types are appended to the blocktable record here - the polyface mesh *requires* it before its
    // vertices and faces can be added, and appending the subd mesh too keeps the entity db-resident either way.
    var btr = (ADB.BlockTableRecord)
      tr.GetObject(_settingsStore.Current.Document.Database.CurrentSpaceId, ADB.OpenMode.ForWrite);

    return points.Count > MAX_POLYFACE_MESH_VERTICES
      ? ConvertToSubDMesh(target, points, btr, tr)
      : ConvertToPolyFaceMesh(target, points, btr, tr);
  }

  private static ADB.PolyFaceMesh ConvertToPolyFaceMesh(
    SOG.Mesh target,
    List<AG.Point3d> points,
    ADB.BlockTableRecord btr,
    ADB.Transaction tr
  )
  {
    ADB.PolyFaceMesh mesh = new();
    mesh.SetDatabaseDefaults();

    // append mesh to blocktable record - necessary before adding vertices and faces
    btr.AppendEntity(mesh);
    tr.AddNewlyCreatedDBObject(mesh, true);

    // add polyfacemesh vertices
    for (int i = 0; i < points.Count; i++)
    {
      var vertex = new ADB.PolyFaceMeshVertex(points[i]);
      if (i < target.colors.Count)
      {
        try
        {
          if (System.Drawing.Color.FromArgb(target.colors[i]) is System.Drawing.Color color)
          {
            vertex.Color = Autodesk.AutoCAD.Colors.Color.FromRgb(color.R, color.G, color.B);
          }
        }
        catch (System.Exception e) when (!e.IsFatal())
        {
          // POC: should we warn user?
          // Couldn't set vertex color, but this should not prevent conversion.
        }
      }

      if (vertex.IsNewObject)
      {
        mesh.AppendVertex(vertex);
        tr.AddNewlyCreatedDBObject(vertex, true);
      }
    }

    // add polyfacemesh faces. vertex index starts at 1 sigh
    int j = 0;
    while (j < target.faces.Count)
    {
      ADB.FaceRecord face;
      if (target.faces[j] == 3) // triangle
      {
        face = new ADB.FaceRecord(
          (short)(target.faces[j + 1] + 1),
          (short)(target.faces[j + 2] + 1),
          (short)(target.faces[j + 3] + 1),
          0
        );
        j += 4;
      }
      else // quad
      {
        face = new ADB.FaceRecord(
          (short)(target.faces[j + 1] + 1),
          (short)(target.faces[j + 2] + 1),
          (short)(target.faces[j + 3] + 1),
          (short)(target.faces[j + 4] + 1)
        );
        j += 5;
      }

      if (face.IsNewObject)
      {
        mesh.AppendFaceRecord(face);
        tr.AddNewlyCreatedDBObject(face, true);
      }
    }

    return mesh;
  }

  // A mesh too large for a PolyFaceMesh [ENG-8836]. The MESH entity takes its whole topology in one call: a vertex
  // array plus a count-prefixed, 0-based face array — the layout Speckle already uses — with 32-bit indices, and
  // carries per-vertex colours on its EntityColor array. Smooth level 0 keeps the faceting exactly as authored.
  private static ADB.SubDMesh ConvertToSubDMesh(
    SOG.Mesh target,
    List<AG.Point3d> points,
    ADB.BlockTableRecord btr,
    ADB.Transaction tr
  )
  {
    using AG.Point3dCollection vertices = new(points.ToArray());
    AG.Int32Collection faceArray = new(GetSubDMeshFaces(target.faces, points.Count));

    ADB.SubDMesh mesh = new();
    mesh.SetDatabaseDefaults();
    mesh.SetSubDMesh(vertices, faceArray, 0);

    if (target.colors.Count == points.Count)
    {
      try
      {
        mesh.VertexColorArray = target
          .colors.Select(argb =>
          {
            var color = System.Drawing.Color.FromArgb(argb);
            return new Autodesk.AutoCAD.Colors.EntityColor(color.R, color.G, color.B);
          })
          .ToArray();
      }
      catch (System.Exception e) when (!e.IsFatal())
      {
        // Couldn't set vertex colors, but this should not prevent conversion (mirrors the polyface mesh path).
      }
    }

    btr.AppendEntity(mesh);
    tr.AddNewlyCreatedDBObject(mesh, true);

    return mesh;
  }

  // Speckle's face list is already the count-prefixed, 0-based layout SetSubDMesh expects, so this only normalizes it:
  // the legacy 0/1 count encoding (triangle/quad), repeated corners, and faces that are truncated or point outside the
  // vertex array. SetSubDMesh takes the topology in one call and rejects the WHOLE mesh on a single bad face, so a
  // malformed face is dropped here instead of costing us the entire surface.
  private static int[] GetSubDMeshFaces(List<int> faces, int vertexCount)
  {
    List<int> result = new(faces.Count);
    List<int> corners = new(4);
    int i = 0;
    while (i < faces.Count)
    {
      int vertexPerFace = faces[i];
      if (vertexPerFace < 3)
      {
        vertexPerFace += 3; // legacy encoding: 0 -> triangle, 1 -> quad
      }

      if (i + vertexPerFace >= faces.Count)
      {
        break; // truncated face list
      }

      corners.Clear();
      int lastCorner = -1;
      for (int j = i + 1; j <= i + vertexPerFace; j++)
      {
        int index = faces[j];
        if (index < 0 || index >= vertexCount)
        {
          corners.Clear(); // face points outside the vertex array - drop it whole
          break;
        }

        // Collapse a repeated corner: a quad written [a, b, c, c] is really a triangle, and a face left with fewer
        // than 3 distinct corners has no area to bake.
        if (index != lastCorner)
        {
          corners.Add(index);
          lastCorner = index;
        }
      }

      if (corners.Count > 3 && corners[0] == lastCorner)
      {
        corners.RemoveAt(corners.Count - 1); // closed face: last corner repeats the first
      }

      if (corners.Count >= 3)
      {
        result.Add(corners.Count);
        result.AddRange(corners);
      }

      i += vertexPerFace + 1;
    }

    return result.ToArray();
  }
}
