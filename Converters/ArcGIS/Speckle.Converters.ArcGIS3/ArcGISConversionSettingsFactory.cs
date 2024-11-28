using ArcGIS.Core.Data;
using ArcGIS.Core.Data.DDL;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Mapping;
using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Logging;

namespace Speckle.Converters.ArcGIS3;

[GenerateAutoInterface]
public class ArcGISConversionSettingsFactory(IHostToSpeckleUnitConverter<ACG.Unit> unitConverter)
  : IArcGISConversionSettingsFactory
{
  public ArcGISConversionSettings Create(Project project, Map map, CRSoffsetRotation activeCRSoffsetRotation) =>
    new(
      project,
      map,
      EnsureOrAddSpeckleDatabase(),
      activeCRSoffsetRotation,
      unitConverter.ConvertOrThrow(activeCRSoffsetRotation.SpatialReference.Unit)
    );

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
      Uri firstDatabasePath = new Uri($"{fGdbPath}/{FGDB_NAME}");

      Uri databasePath = ValidateDatabasePath(firstDatabasePath);
      return databasePath;
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

  public Uri ValidateDatabasePath(Uri originalGatabasePath)
  {
    var fGdbName = originalGatabasePath.Segments[^1];
    var parentFolder = Path.GetDirectoryName(originalGatabasePath.AbsolutePath);
    if (parentFolder == null)
    {
      // POC: customize the exception type
      throw new ArgumentException($"Invalid path: {originalGatabasePath}");
    }

    Uri databasePath = originalGatabasePath;
    Item folderToAdd = ItemFactory.Instance.Create(parentFolder);
    if (folderToAdd is null)
    {
      // ArcGIS API doesn't show it as nullable, but it is
      // Likely the project location is inaccessible  with not enough permissions
      // Store inside Speckle folder

      string speckleFolder = SpecklePathProvider.UserSpeckleFolderPath; //Path.GetTempPath();
      // create folder in Speckle repo
      string speckleArcgisFolder = Path.Join(speckleFolder, $"ArcGIS_gdb");
      bool existsArcgisFolder = Directory.Exists(speckleArcgisFolder);
      if (!existsArcgisFolder)
      {
        Directory.CreateDirectory(speckleArcgisFolder);
      }

      // create a project-specific folder
      string projectFolderName;
      string? folderContainingProject = Path.GetDirectoryName(parentFolder);
      if (folderContainingProject == null)
      {
        projectFolderName = "default";
      }
      else
      {
        projectFolderName = Path.GetRelativePath(folderContainingProject, parentFolder);
      }

      string tempParentFolder = Path.Join(speckleArcgisFolder, $"{projectFolderName}");
      bool exists = Directory.Exists(tempParentFolder);
      if (!exists)
      {
        Directory.CreateDirectory(tempParentFolder);
      }

      // repeat: try adding a folder item again
      folderToAdd = ItemFactory.Instance.Create(tempParentFolder);
      if (folderToAdd is null)
      {
        throw new ArgumentException(
          $"Project path: '{parentFolder}' and Temp folder: '{tempParentFolder}' likely don't have write permissions."
        );
      }
      databasePath = new Uri(Path.Join(tempParentFolder, fGdbName), UriKind.Absolute);
    }

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

    return databasePath;
  }

  public Uri AddDatabaseToProject(Uri databasePath)
  {
    // Add a folder connection to a project
    var parentFolder = Path.GetDirectoryName(databasePath.AbsolutePath);
    var fGdbName = databasePath.Segments[^1];
    Item folderToAdd = ItemFactory.Instance.Create(parentFolder);
    Project.Current.AddItem(folderToAdd as IProjectItem);

    // Add a file geodatabase or a SQLite or enterprise database connection to a project
    try
    {
      var gdbToAdd = folderToAdd
        .GetItems()
        .FirstOrDefault(folderItem => folderItem.Name.Equals(fGdbName, StringComparison.Ordinal));

      if (gdbToAdd is not null)
      {
        var addedGeodatabase = Project.Current.AddItem(gdbToAdd as IProjectItem);
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
