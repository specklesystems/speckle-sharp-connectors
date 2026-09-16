using Speckle.Converters.Common;
using Speckle.Converters.MicroStation.Settings;

namespace Speckle.Converters.MicroStation.Services;

/// <summary>
/// The single coordinate choke point for the whole ToSpeckle pipeline — the managed equivalent of
/// dgnextract's <c>UnitsCtx</c>. Every DGN coordinate flows through <see cref="MapPoint(BG.DPoint3d)"/>:
/// <list type="number">
/// <item>the ambient transform stack (reference-attachment placement, nested-cell baking, graphics-
/// processor announcements) composes left-to-right and lands the point in the master frame,</item>
/// <item>the master model's global origin is subtracted — suppressed while a shared-cell DEFINITION
/// is being built, because a definition lives in its own local frame and the shift belongs on the
/// instance transform (dgnextract's <c>defFrameDepth</c> rule),</item>
/// <item>the UOR→master-unit scale is applied (see <see cref="MicroStationConversionSettings.UorPerMaster"/>).</item>
/// </list>
/// Scoped per send operation (fresh stack per operation, same lifetime as the settings store).
/// </summary>
public class GeometryMapper(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
{
  private readonly List<BG.DTransform3d> _ambient = [];
  private int _definitionFrameDepth;

  /// <summary>Composed ambient transform, or null when the stack is empty (the common case).</summary>
  private BG.DTransform3d? _composed;

  public string Units => settingsStore.Current.SpeckleUnits;

  private double UorScale
  {
    get
    {
      double uorPerMaster = settingsStore.Current.UorPerMaster;
      return uorPerMaster > 0 ? 1.0 / uorPerMaster : 1.0;
    }
  }

  public bool InDefinitionFrame => _definitionFrameDepth > 0;

  /// <summary>UORs per master unit — for expressing master-unit tolerances in UOR space
  /// (stroking/facetting happens on unscaled UOR geometry).</summary>
  public double UorPerMasterForTolerances() => Math.Max(settingsStore.Current.UorPerMaster, 1e-12);

  /// <summary>Pushes a placement onto the ambient stack (attachment transform, nested shared-cell
  /// baking, graphics announcements). Dispose the returned scope to pop.</summary>
  public IDisposable PushTransform(BG.DTransform3d transform)
  {
    _ambient.Add(transform);
    Recompose();
    return new PopScope(this, popTransform: true, popDefinition: false, restoreAmbient: null);
  }

  /// <summary>
  /// Enters a shared-cell definition frame: the ambient stack is suspended (the occurrence transform
  /// belongs on the INSTANCE, baking it into members would apply it twice) and the global-origin
  /// subtract is suppressed. Nested shared cells inside the definition then push onto a clean stack.
  /// </summary>
  public IDisposable PushDefinitionFrame()
  {
    var suspended = new List<BG.DTransform3d>(_ambient);
    _ambient.Clear();
    _definitionFrameDepth++;
    Recompose();
    return new PopScope(this, popTransform: false, popDefinition: true, restoreAmbient: suspended);
  }

  /// <summary>UOR point in the current frame → Speckle point (master units, master frame).</summary>
  public SOG.Point MapPoint(BG.DPoint3d p)
  {
    var (x, y, z) = MapXyz(p);
    return new SOG.Point
    {
      x = x,
      y = y,
      z = z,
      units = Units,
    };
  }

  /// <summary>UOR point → raw (x, y, z) master-unit triple, for flat coordinate lists.</summary>
  public (double X, double Y, double Z) MapXyz(BG.DPoint3d p)
  {
    if (_composed is BG.DTransform3d t)
    {
      t.Multiply(out BG.DPoint3d transformed, p);
      p = transformed;
    }

    double scale = UorScale;
    var s = settingsStore.Current;
    if (InDefinitionFrame)
    {
      return (p.X * scale, p.Y * scale, p.Z * scale);
    }
    return ((p.X - s.GlobalOriginX) * scale, (p.Y - s.GlobalOriginY) * scale, (p.Z - s.GlobalOriginZ) * scale);
  }

  /// <summary>Direction through the ambient linear part only (no translation, no origin, no scale),
  /// then normalized — for plane normals / axis vectors.</summary>
  public SOG.Vector MapDirection(BG.DVector3d v)
  {
    if (_composed is BG.DTransform3d t)
    {
      v = t.Matrix.Multiply(v);
    }
    double len = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
    if (len > 0)
    {
      return new SOG.Vector
      {
        x = v.X / len,
        y = v.Y / len,
        z = v.Z / len,
        units = Units,
      };
    }
    return new SOG.Vector
    {
      x = 0,
      y = 0,
      z = 1,
      units = Units,
    };
  }

  /// <summary>Vector through the ambient linear part with UOR scaling (magnitude matters — ellipse axes).</summary>
  public BG.DVector3d MapVectorRaw(BG.DVector3d v)
  {
    if (_composed is BG.DTransform3d t)
    {
      v = t.Matrix.Multiply(v);
    }
    double scale = UorScale;
    return new BG.DVector3d
    {
      X = v.X * scale,
      Y = v.Y * scale,
      Z = v.Z * scale,
    };
  }

  /// <summary>
  /// A UOR-frame placement transform (e.g. a shared-cell basis transform) → the 16-double row-major
  /// Speckle matrix whose translation is in master units of the master frame. Composes the current
  /// ambient stack on the left, so an instance placed while inside a baked nested context still lands
  /// in the master frame (dgnextract's <c>Placement::multiply(occ.xform, placement)</c> + toSpeckle16).
  /// </summary>
  public List<double> ToSpeckleMatrix(BG.DTransform3d placement, bool subtractGlobalOrigin = true)
  {
    if (_composed is BG.DTransform3d ambient)
    {
      placement = BG.DTransform3d.Multiply(ambient, placement);
    }

    var s = settingsStore.Current;
    double scale = UorScale;
    // A nested placement (inside a definition frame) is definition-local: the global-origin shift
    // belongs on the top-level instance only (dgnextract's defFrameDepth rule).
    bool subtract = subtractGlobalOrigin && !InDefinitionFrame;
    BG.DMatrix3d m = placement.Matrix;
    BG.DPoint3d tr = placement.Translation;
    double tx = (tr.X - (subtract ? s.GlobalOriginX : 0)) * scale;
    double ty = (tr.Y - (subtract ? s.GlobalOriginY : 0)) * scale;
    double tz = (tr.Z - (subtract ? s.GlobalOriginZ : 0)) * scale;

    // Row-major 4x4. The linear part is unit-free (definition geometry is already in master units),
    // only the translation carries units.
    BG.DVector3d rx = m.RowX;
    BG.DVector3d ry = m.RowY;
    BG.DVector3d rz = m.RowZ;
    return [rx.X, rx.Y, rx.Z, tx, ry.X, ry.Y, ry.Z, ty, rz.X, rz.Y, rz.Z, tz, 0, 0, 0, 1];
  }

  /// <summary>
  /// A UOR-frame placement transform → the row-major <see cref="Speckle.DoubleNumerics.Matrix4x4"/>
  /// an <c>InstanceProxy</c> carries. Same math as <see cref="ToSpeckleMatrix"/>.
  /// </summary>
  public Speckle.DoubleNumerics.Matrix4x4 ToInstanceMatrix(BG.DTransform3d placement, bool subtractGlobalOrigin = true)
  {
    List<double> m = ToSpeckleMatrix(placement, subtractGlobalOrigin);
    return new Speckle.DoubleNumerics.Matrix4x4(
      m[0],
      m[1],
      m[2],
      m[3],
      m[4],
      m[5],
      m[6],
      m[7],
      m[8],
      m[9],
      m[10],
      m[11],
      m[12],
      m[13],
      m[14],
      m[15]
    );
  }

  /// <summary>True when the ambient stack would distort circles/ellipses non-affinely — never the
  /// case for affine transforms, so this only reports a (near-)singular linear part.</summary>
  public bool AmbientIsDegenerate()
  {
    if (_composed is BG.DTransform3d t)
    {
      return Math.Abs(t.Matrix.Determinant()) < 1e-12;
    }
    return false;
  }

  private void Recompose()
  {
    if (_ambient.Count == 0)
    {
      _composed = null;
      return;
    }
    BG.DTransform3d acc = _ambient[0];
    for (int i = 1; i < _ambient.Count; i++)
    {
      acc = BG.DTransform3d.Multiply(acc, _ambient[i]);
    }
    _composed = acc;
  }

  private sealed class PopScope(
    GeometryMapper owner,
    bool popTransform,
    bool popDefinition,
    List<BG.DTransform3d>? restoreAmbient
  ) : IDisposable
  {
    private bool _disposed;

    public void Dispose()
    {
      if (_disposed)
      {
        return;
      }
      _disposed = true;
      if (popTransform && owner._ambient.Count > 0)
      {
        owner._ambient.RemoveAt(owner._ambient.Count - 1);
      }
      if (popDefinition)
      {
        owner._definitionFrameDepth--;
        owner._ambient.Clear();
        if (restoreAmbient != null)
        {
          owner._ambient.AddRange(restoreAmbient);
        }
      }
      owner.Recompose();
    }
  }
}
