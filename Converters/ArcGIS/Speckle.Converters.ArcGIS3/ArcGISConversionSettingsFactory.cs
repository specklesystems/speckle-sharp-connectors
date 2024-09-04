using ArcGIS.Core.Data;
using ArcGIS.Core.Data.DDL;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.ArcGIS3;

[GenerateAutoInterface]
public class ArcGISConversionSettingsFactory(IHostToSpeckleUnitConverter<ACG.Unit> unitConverter)
  : IArcGISConversionSettingsFactory
{
  public ArcGISConversionSettings Create(Project project, Map map, CRSoffsetRotation activeCRSoffsetRotation) =>
    new()
    {
      Project = project,
      Map = map,
      ActiveCRSoffsetRotation = activeCRSoffsetRotation,
      SpeckleDatabasePath = EnsureOrAddSpeckleDatabase(),
      SpeckleUnits = unitConverter.ConvertOrThrow(activeCRSoffsetRotation.SpatialReference.Unit)
    };

  public Uri EnsureOrAddSpeckleDatabase()
  {
    return AddDatabaseToProject(GetDatabasePath());
  }

  private const string FGDB_NAME = "Speckle.gdb";

  public Uri GetDatabasePath()
  {
    try
    {
      var parentDirectory = Directory.GetParent(Project.Current.URI);
      if (parentDirectory == null)
      {
        throw new ArgumentException($"Project directory {Project.Current.URI} not found");
      }
      var fGdbPath = new Uri(parentDirectory.FullName);
      return new Uri($"{fGdbPath}/{FGDB_NAME}");
    }
    catch (Exception ex)
      when (ex
          is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or System.Security.SecurityException
      )
    {
      throw;
    }
  }

  public Uri AddDatabaseToProject(Uri databasePath)
  {
    // Create a FileGeodatabaseConnectionPath with the name of the file geodatabase you wish to create
    FileGeodatabaseConnectionPath fileGeodatabaseConnectionPath = new(databasePath);
    // Create actual database in the specified Path unless already exists
    try
    {
      Geodatabase geodatabase = SchemaBuilder.CreateGeodatabase(fileGeodatabaseConnectionPath);
      geodatabase.Dispose();
    }
    catch (ArcGIS.Core.Data.Exceptions.GeodatabaseWorkspaceException)
    {
      // geodatabase already exists, do nothing
    }

    // Add a folder connection to a project
    var parentFolder = Path.GetDirectoryName(databasePath.AbsolutePath);
    if (parentFolder == null)
    {
      // POC: customize the exception type
      throw new ArgumentException($"Invalid path: {databasePath}");
    }
    var fGdbName = databasePath.Segments[^1];
    Item folderToAdd = ItemFactory.Instance.Create(parentFolder);
    // POC: QueuedTask
    QueuedTask.Run(() => Project.Current.AddItem(folderToAdd as IProjectItem));

    // Add a file geodatabase or a SQLite or enterprise database connection to a project
    try
    {
      var gdbToAdd = folderToAdd
        .GetItems()
        .FirstOrDefault(folderItem => folderItem.Name.Equals(fGdbName, StringComparison.Ordinal));

      if (gdbToAdd is not null)
      {
        // POC: QueuedTask
        var addedGeodatabase = QueuedTask.Run(() => Project.Current.AddItem(gdbToAdd as IProjectItem));
      }
    }
    catch (NullReferenceException ex)
    {
      throw new InvalidOperationException(
        "Make sure your ArcGIS Pro project folder has permissions to create a new database. E.g., project cannot be saved in a Google Drive folder.",
        ex
      );
    }

    return databasePath;
  }
}
