using System.Diagnostics.CodeAnalysis;
using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;
using Speckle.DoubleNumerics;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.RevitShared.ToHost.TopLevel;

public class MeshConverterToHost : ITypedConverter<SOG.Mesh, List<GeometryObject>>
{
  private readonly RevitToHostCacheSingleton _revitToHostCacheSingleton;
  private readonly ScalingServiceToHost _scalingServiceToHost;
  private readonly IReferencePointConverter _referencePointConverter;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private const double PLANAR_TOLERANCE = 1e-9; // tune if needed, added to avoid numeric noise
  private const bool ALLOW_VERTEX_COLOR_OVERRIDE = true; // flip to true if colors should win

  private Document? _lastDoc; // if this converter instance is used across open documents, we'll want to invalidate the material cache

  public MeshConverterToHost(
    RevitToHostCacheSingleton revitToHostCacheSingleton,
    ScalingServiceToHost scalingServiceToHost,
    IReferencePointConverter referencePointConverter,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _revitToHostCacheSingleton = revitToHostCacheSingleton;
    _scalingServiceToHost = scalingServiceToHost;
    _referencePointConverter = referencePointConverter;
    _converterSettings = converterSettings;
  }

  public List<GeometryObject> Convert(SOG.Mesh mesh)
  {
    const TessellatedShapeBuilderTarget TARGET = TessellatedShapeBuilderTarget.Mesh;
    const TessellatedShapeBuilderFallback FALLBACK = TessellatedShapeBuilderFallback.Salvage;

    using var tsb = new TessellatedShapeBuilder();
    tsb.Fallback = FALLBACK;
    tsb.Target = TARGET;
    tsb.GraphicsStyleId = ElementId.InvalidElementId;
    // tsb.OpenConnectedFaceSet(false);

    var vertices = ArrayToPoints(mesh.vertices, mesh.units);
    var vertColors = DecodeVertexColors(mesh.colors);

    // optional default material from cache
    ElementId defaultMat = ElementId.InvalidElementId;
    if (
      _revitToHostCacheSingleton.MaterialsByObjectId.TryGetValue(
        mesh.applicationId ?? mesh.id.NotNull(),
        out var mapped
      )
    )
    {
      defaultMat = mapped;
    }

    bool hasExplicitMat = defaultMat != ElementId.InvalidElementId;

    var facesByMat = new Dictionary<ElementId, List<IList<XYZ>>>();

    int i = 0;
    while (i < mesh.faces.Count)
    {
      int faceVertexCount = mesh.faces[i];
      if (faceVertexCount < 3)
      {
        faceVertexCount += 3;
      }

      var faceIdx = mesh.faces.GetRange(i + 1, faceVertexCount);
      var points = new XYZ[faceVertexCount];
      for (int k = 0; k < faceVertexCount; k++)
      {
        points[k] = vertices[faceIdx[k]];
      }

      var faceMaterial = FaceMat(faceIdx);
      switch (faceVertexCount)
      {
        case 4 when IsNonPlanarQuad(points):
        {
          // Non-planar quads will be triangulated as it's more desirable than
          // TessellatedShapeBuilder.Build's attempt to make them planar.
          AddFace([points[0], points[1], points[3]], faceMaterial);
          AddFace([points[1], points[2], points[3]], faceMaterial);
          break;
        }
        case > 4 when !IsPlanarNgon(points):
        {
          for (int k = 1; k < faceVertexCount - 1; k++)
          {
            AddFace([points[0], points[k], points[k + 1]], faceMaterial);
          }
          break;
        }
        default:
        {
          AddFace(points, faceMaterial);
          break;
        }
      }

      i += faceVertexCount + 1;
    }

    var all = new List<GeometryObject>();

    foreach (var kv in facesByMat)
    {
      using var perMat = new TessellatedShapeBuilder();
      perMat.Fallback = FALLBACK;
      perMat.Target = TARGET;
      perMat.GraphicsStyleId = ElementId.InvalidElementId;

      perMat.OpenConnectedFaceSet(true);
      foreach (var tf in kv.Value.Select(pts => new TessellatedFace(pts, kv.Key)).Where(tf => tf.IsValidObject))
      {
        perMat.AddFace(tf);
      }

      perMat.CloseConnectedFaceSet();
      perMat.Build();

      all.AddRange(perMat.GetBuildResult().GetGeometricalObjects());
    }

    return all;

    void AddFace(IList<XYZ> pts, ElementId mat)
    {
      if (!facesByMat.TryGetValue(mat, out var list))
      {
        facesByMat[mat] = list = [];
      }

      list.Add(pts);
    }

    // local helper to pick a face material from vertex colors
    [SuppressMessage("ReSharper", "RedundantLogicalConditionalExpressionOperand")]
    ElementId FaceMat(IList<int> idx)
    {
      int vCount = vertColors.Length;
      var hasColors = vCount > 0;

      if (!hasColors || hasExplicitMat && !ALLOW_VERTEX_COLOR_OVERRIDE)
      {
        return defaultMat;
      }

      int sr = 0,
        sg = 0,
        sb = 0,
        c = 0;
      foreach (var v in idx)
      {
        if ((uint)v >= (uint)vCount)
        {
          continue;
        }

        var vc = vertColors[v];
        sr += vc.Red;
        sg += vc.Green;
        sb += vc.Blue;
        c++;
      }

      if (c == 0)
      {
        return defaultMat;
      }

      byte r = Quant((byte)(sr / c));
      byte g = Quant((byte)(sg / c));
      byte b = Quant((byte)(sb / c));
      return GetOrCreateMaterial(_converterSettings.Current.Document, r, g, b);
    }
  }

  private static bool IsNonPlanarQuad(IList<XYZ> points)
  {
    if (points.Count != 4)
    {
      return false;
    }

    var matrix = new Matrix4x4(
      points[0].X,
      points[1].X,
      points[2].X,
      points[3].X,
      points[0].Y,
      points[1].Y,
      points[2].Y,
      points[3].Y,
      points[0].Z,
      points[1].Z,
      points[2].Z,
      points[3].Z,
      1,
      1,
      1,
      1
    );

    return Math.Abs(matrix.GetDeterminant()) > PLANAR_TOLERANCE;
  }

  private static bool IsPlanarNgon(IList<XYZ> vertices)
  {
    int n = vertices.Count;
    if (n < 4)
    {
      return true; // 3 points always define a plane
    }

    // Newell’s method for robust best-fit plane =>
    // https://www.realtimerendering.com/resources/GraphicsGems/gemsiii/newell.c
    double normalX = 0,
      normalY = 0,
      normalZ = 0;
    for (int i = 0, j = n - 1; i < n; j = i, i++)
    {
      var u = vertices[i];
      var v = vertices[j];
      normalX += (v.Y - u.Y) * (v.Z + u.Z);
      normalY += (v.Z - u.Z) * (v.X + u.X);
      normalZ += (v.X - u.X) * (v.Y + u.Y);
    }

    var length = Math.Sqrt(normalX * normalX + normalY * normalY + normalZ * normalZ);
    if (length < 1e-12)
    {
      return true; // degenerate polygon; treat as planar
    }

    normalX /= length;
    normalY /= length;
    normalZ /= length;

    var pointOnPlane = vertices[0];
    double normalisedPlane = -(normalX * pointOnPlane.X + normalY * pointOnPlane.Y + normalZ * pointOnPlane.Z);

    // max signed distance of all vertices to plane
    double maxSignedDistance = 0;
    for (int i = 1; i < n; i++)
    {
      var p = vertices[i];
      double distance = normalX * p.X + normalY * p.Y + normalZ * p.Z + normalisedPlane;
      maxSignedDistance = Math.Max(maxSignedDistance, Math.Abs(distance));
      if (maxSignedDistance > PLANAR_TOLERANCE)
      {
        return false;
      }
    }

    return true;
  }

  private XYZ[] ArrayToPoints(IList<double> arr, string units)
  {
    if (arr.Count % 3 != 0)
    {
      throw new ValidationException("Array malformed: length%3 != 0.");
    }

    XYZ[] points = new XYZ[arr.Count / 3];
    var fTypeId = _scalingServiceToHost.UnitsToNative(units);

    for (int i = 2, k = 0; i < arr.Count; i += 3)
    {
      // Scale the coordinates first
      var x = _scalingServiceToHost.ScaleToNative(arr[i - 2], fTypeId);
      var y = _scalingServiceToHost.ScaleToNative(arr[i - 1], fTypeId);
      var z = _scalingServiceToHost.ScaleToNative(arr[i], fTypeId);

      // Create the XYZ point
      var point = new XYZ(x, y, z);

      // Apply reference point transformation (this is the crucial part)
      points[k++] = _referencePointConverter.ConvertToInternalCoordinates(point, true);
    }

    return points;
  }

  private readonly Dictionary<int, ElementId> _matCache = new();

  private static Color[] DecodeVertexColors(IList<int>? argb)
  {
    if (argb == null)
    {
      return [];
    }

    var outArr = new Color[argb.Count];
    for (int i = 0; i < argb.Count; i++)
    {
      uint v = unchecked((uint)argb[i]); // Speckle stores ARGB in a signed int
      byte r = (byte)((v >> 16) & 0xFF);
      byte g = (byte)((v >> 8) & 0xFF);
      byte b = (byte)(v & 0xFF);

      outArr[i] = new Color(r, g, b);
    }

    return outArr;
  }

  private static byte Quant(byte v, int step = 17)
  {
    int q = (int)Math.Round(v / (double)step) * step;
    return (byte)Math.Max(0, Math.Min(255, q));
  }

  private ElementId GetOrCreateMaterial(Document doc, byte r, byte g, byte b)
  {
    if (!ReferenceEquals(doc, _lastDoc)) // essentially a document change check hack
    {
      _matCache.Clear();
      _lastDoc = doc;
    }

    int key = (r << 16) | (g << 8) | b;
    if (_matCache.TryGetValue(key, out var id))
    {
      return id;
    }

    string name = $"Speckle_DS_{r}_{g}_{b}";

    Material? existing;
    using (var filteredElementCollector = new FilteredElementCollector(doc))
    {
      filteredElementCollector.OfClass(typeof(Material)); // add the filter on the same instance
      existing = filteredElementCollector
        .Cast<Material>() // enumerate inside the using
        .FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    if (existing != null)
    {
      return _matCache[key] = existing.Id;
    }

    ElementId mid;
    if (doc.IsModifiable)
    {
      using var st = new SubTransaction(doc);
      st.Start();
      mid = CreateMaterialWithColor(doc, name, r, g, b);
      st.Commit();
    }
    else
    {
      using var t = new Transaction(doc, "Create DS Material");
      t.Start();
      mid = CreateMaterialWithColor(doc, name, r, g, b);
      t.Commit();
    }

    return _matCache[key] = mid;

    static ElementId CreateMaterialWithColor(Document doc, string name, byte r, byte g, byte b)
    {
      var materialId = Material.Create(doc, name);
      ((Material)doc.GetElement(materialId)).Color = new Color(r, g, b);
      return materialId;
    }
  }
}
