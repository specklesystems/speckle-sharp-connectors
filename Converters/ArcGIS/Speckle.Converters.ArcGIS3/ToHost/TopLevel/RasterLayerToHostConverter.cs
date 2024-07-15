using ArcGIS.Core.Data;
using ArcGIS.Core.Data.DDL;
using ArcGIS.Core.Data.Raster;
using ArcGIS.Desktop.Core.Geoprocessing;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using Objects.GIS;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Core.Logging;
using Speckle.Core.Models;

namespace Speckle.Converters.ArcGIS3.ToHost.TopLevel;

[NameAndRankValue(nameof(RasterLayer), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class RasterLayerToHostConverter : IToHostTopLevelConverter, ITypedConverter<RasterLayer, string>
{
  private readonly IConversionContextStack<ArcGISDocument, ACG.Unit> _contextStack;

  public RasterLayerToHostConverter(IConversionContextStack<ArcGISDocument, ACG.Unit> contextStack)
  {
    _contextStack = contextStack;
  }

  public object Convert(Base target) => Convert((RasterLayer)target);

  public string Convert(RasterLayer target)
  {
    /// Add a folder connection to a project
    // Item folderToAdd = ItemFactory.Instance.Create(@"C:\Data\Oregon\Counties\Streets");
    // bool wasAdded = await QueuedTask.Run(() => Project.Current.AddItem(folderToAdd as IProjectItem));

    FileGeodatabaseConnectionPath fileGeodatabaseConnectionPath =
      new(_contextStack.Current.Document.SpeckleDatabasePath);
    Geodatabase geodatabase = new(fileGeodatabaseConnectionPath);
    // SchemaBuilder schemaBuilder = new(geodatabase);

    // get RasterElement and it's name
    RasterElement rasterElement = (RasterElement)target.elements[0];
    string rasterName = "speckleRASTER_" + target.id + ".tif";

    // create Spatial Reference (i.e. Coordinate Reference System - CRS)
    string wktString = string.Empty;
    if (target.rasterCrs is not null && target.rasterCrs.wkt is not null)
    {
      wktString = target.rasterCrs.wkt;
    }
    ACG.SpatialReference spatialRef = ACG.SpatialReferenceBuilder.CreateSpatialReference(wktString);

    // delete RasterDataset if already exists
    try
    {
      RasterDataset rasterDatasetFound = geodatabase.OpenDataset<RasterDataset>(rasterName);
      // RasterDatasetDefinition fClassDefinition = geodatabase.GetDefinition<RasterDatasetDefinition>(rasterName);
      RasterDatasetDefinition rasterDatasetDefinition = rasterDatasetFound.GetDefinition();
      // ShapeDescription existingDescription = new(rasterDatasetDefinition);
      // schemaBuilder.Delete(existingDescription);
      // schemaBuilder.Build();
    }
    catch (Exception ex) when (!ex.IsFatal()) //(GeodatabaseTableException) //GeodatabaseCatalogDatasetException
    {
      // "The table was not found."
      // delete Table if already exists
      try
      {
        // TableDefinition fClassDefinition = geodatabase.GetDefinition<TableDefinition>(rasterName);
        // TableDescription existingDescription = new(fClassDefinition);
        // schemaBuilder.Delete(existingDescription);
        // schemaBuilder.Build();
      }
      catch (Exception ex2) when (!ex2.IsFatal()) //(GeodatabaseTableException)
      {
        // "The table was not found.", do nothing
      }
    }

    // POC:
    string outPath = _contextStack.Current.Document.SpeckleDatabasePath.AbsolutePath
      .Replace('/', '\\')
      .Split("Speckle.gdb")[0];
    // https://community.esri.com/t5/arcgis-pro-sdk-questions/create-rasterdataset/td-p/780214
    // most comprehensive docs: https://webhelp.esri.com/arcgisdesktop/9.3/index.cfm?TopicName=create_raster_dataset_(data_management)
    IReadOnlyList<string> parameters = Geoprocessing.MakeValueArray(
      $"{outPath}", // Output path
      rasterName, // Raster name
      rasterElement.x_resolution, // Cellsize
      "8_BIT_UNSIGNED", // pixel type
      spatialRef, // Spatial reference,
      rasterElement.band_count, //, // Bands count,
      "#",
      "PYRAMIDS -1 NEAREST JPEG",
      $"{System.Convert.ToString(rasterElement.x_size).Split(".")[0]} {System.Convert.ToString(rasterElement.y_size).Split(".")[0]}",
      "NONE",
      $"{rasterElement.x_origin} {rasterElement.y_origin}"
    );

    float yMin = rasterElement.y_origin;
    float yMax = rasterElement.y_origin + rasterElement.y_size * rasterElement.y_size;
    if (yMax < yMin)
    {
      yMin = rasterElement.y_origin + rasterElement.y_size * rasterElement.y_size;
      yMax = rasterElement.y_origin;
    }

    IReadOnlyList<KeyValuePair<string, string>> environments = Geoprocessing.MakeEnvironmentArray(
      outputCoordinateSystem: spatialRef,
      // nodata: rasterElement.noDataValue[0],
      // XYResolution: $"{System.Convert.ToString(rasterElement.x_size).Split(".")[0]} {System.Convert.ToString(rasterElement.y_size).Split(".")[0]}",
      extent: $"{rasterElement.x_origin} {yMin} {rasterElement.x_origin + rasterElement.x_resolution * rasterElement.x_size} {yMax}"
    //
    );

    // bringing back Queued Task, as top-level operation will not be Queued anymore
    // for performance reasons - top-level Queued Task freezes UI for too long
    var gpResult = QueuedTask
      .Run(() =>
      {
        var x = Geoprocessing
          .ExecuteToolAsync("CreateRasterDataset_management", parameters, null, CancelableProgressor.None)
          .Result;
        // Geoprocessing.ShowMessageBox(x.Messages, "Contents", GPMessageBoxStyle.Default, "Window Title");
        return x;
      })
      .Result;

    // Open the raster dataset.
    RasterDataset rasterDataset = geodatabase.OpenDataset<RasterDataset>(rasterName);

    // Create a full raster from the raster dataset.
    ArcGIS.Core.Data.Raster.Raster raster = rasterDataset.CreateFullRaster();

    // Calculate size of pixel block to create. Use 128 or height/width of the raster, whichever is smaller.
    var height = raster.GetHeight();
    var width = raster.GetWidth();
    int pixelBlockHeight = rasterElement.y_size; //height > 128 ? 128 : height;
    int pixelBlockWidth = rasterElement.x_size; // width > 128 ? 128 : width;

    /*
    QueuedTask.Run(() =>
    {
      // Create the raster cursor using the height and width calculated.
      RasterCursor rasterCursor = raster.CreateCursor(pixelBlockWidth, pixelBlockHeight);
      int bandCount = 0;
      // Use a do-while loop to iterate through the pixel blocks of the raster using the raster cursor.
      do
      {
        // Get the current pixel block from the cursor. (1 per band)
        using (PixelBlock currentPixelBlock = rasterCursor.Current)
        {
          // Do something with the pixel block...
          // currentPixelBlock.SetPixelData(bandCount, currentPixelBlock.GetPixelData(bandCount, false));
          bandCount += 1;
        }

        // Once you are done, move to the next pixel block.
      } while (rasterCursor.MoveNext());
    });
    */

    /*
    // Read pixel values from the raster dataset into the pixel block starting from the given top left corner.
    raster.Read(0, 0, currentPixelBlock);

    // Do something with the pixel block...
    currentPixelBlock.SetPixelData();

    // Write the pixel block to the raster dataset starting from the given top left corner.
    raster.Write(0, 0, currentPixelBlock);
    */

    return rasterName;
  }
}
