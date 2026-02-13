using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Speckle.Converters.Civil3dShared;
using Speckle.Converters.Common;
using Speckle.Sdk.Common;
using Surface = Autodesk.Civil.DatabaseServices.Surface;

namespace Speckle.Connectors.Civil3dShared.HostApp;

public class CutFillHelper
{
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;

  public CutFillHelper(IConverterSettingsStore<Civil3dConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public Dictionary<string, double> GetVolumesFromVolumeSurface(string surfaceName)
  {
    Database db = _settingsStore.Current.Document.Database;
    var results = new Dictionary<string, double>();

    using Transaction tr = db.TransactionManager.StartTransaction();

    var volumeSurfaceId = GetVolumeSurfaceByName(db, surfaceName);

    if (volumeSurfaceId == ObjectId.Null)
    {
      tr.Commit();
      return results;
    }

    var volSurface = tr.GetObject(volumeSurfaceId, OpenMode.ForRead) as TinVolumeSurface;
    VolumeSurfaceProperties props = volSurface.NotNull().GetVolumeProperties();
    // var props = volSurface?.Analysis.;

    Units.GetConversionFactor(Units.Millimeters, _settingsStore.Current.SpeckleUnits);
    results["CutVolume"] = props.AdjustedCutVolume / 27.0;
    results["FillVolume"] = props.AdjustedFillVolume / 27.0;
    results["NetVolume"] = props.AdjustedNetVolume / 27.0;

    tr.Commit();

    return results;
  }

  public List<string> GetAllVolumeSurfaceNames(Database db)
  {
    var volumeSurfaceNames = new List<string>();

    using Transaction tr = db.TransactionManager.StartTransaction();

    var civilDoc = CivilApplication.ActiveDocument;

    foreach (ObjectId surfaceId in civilDoc.GetSurfaceIds())
    {
      var volSurface = tr.GetObject(surfaceId, OpenMode.ForRead) as TinVolumeSurface;

      if (volSurface != null)
      {
        volumeSurfaceNames.Add(volSurface.Name);
      }
    }

    tr.Commit();

    return volumeSurfaceNames;
  }

  private ObjectId GetVolumeSurfaceByName(Database db, string surfaceName)
  {
    using Transaction tr = db.TransactionManager.StartTransaction();

    var civilDoc = CivilApplication.ActiveDocument;

    foreach (ObjectId surfaceId in civilDoc.GetSurfaceIds())
    {
      var surface = tr.GetObject(surfaceId, OpenMode.ForRead) as Surface;

      if (surface is TinVolumeSurface && surface.Name == surfaceName)
      {
        tr.Commit();
        return surfaceId;
      }
    }

    tr.Commit();

    return ObjectId.Null;
  }
}
