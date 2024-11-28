using System.Collections;
using ArcGIS.Core.Data;
using ArcGIS.Core.Data.Exceptions;
using Speckle.InterfaceGenerator;
using Speckle.Objects.GIS;
using Speckle.Sdk;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.GraphTraversal;
using FieldDescription = ArcGIS.Core.Data.DDL.FieldDescription;

namespace Speckle.Converters.ArcGIS3.Utils;

[GenerateAutoInterface]
public class ArcGISFieldUtils : IArcGISFieldUtils
{
  private readonly ICharacterCleaner _characterCleaner;
  private const string FID_FIELD_NAME = "OBJECTID";

  public ArcGISFieldUtils(ICharacterCleaner characterCleaner)
  {
    _characterCleaner = characterCleaner;
  }

  public Dictionary<string, object?> GetAttributesViaFunction(
    ObjectConversionTracker trackerItem,
    List<(FieldDescription, Func<Base, object?>)> fieldsAndFunctions
  )
  {
    // set and pass attributes
    Dictionary<string, object?> attributes = new();
    foreach ((FieldDescription field, Func<Base, object?> function) in fieldsAndFunctions)
    {
      string key = field.AliasName;
      attributes[key] = function(trackerItem.Base);
      if (attributes[key] is null && key == "Speckle_ID")
      {
        attributes[key] = trackerItem.Base.id;
      }
    }

    return attributes;
  }

  public RowBuffer AssignFieldValuesToRow(
    RowBuffer rowBuffer,
    List<FieldDescription> fields,
    Dictionary<string, object?> attributes
  )
  {
    foreach (FieldDescription field in fields)
    {
      // try to assign values to writeable fields
      if (attributes is not null)
      {
        string key = field.AliasName; // use Alias, as Name is simplified to alphanumeric
        FieldType fieldType = field.FieldType;
        var value = attributes[key];

        try
        {
          rowBuffer[key] = GISAttributeFieldType.SpeckleValueToNativeFieldType(fieldType, value);
        }
        catch (GeodatabaseFeatureException)
        {
          //'The value type is incompatible.'
          // log error!
          rowBuffer[key] = null;
        }
        catch (GeodatabaseFieldException)
        {
          // non-editable Field, do nothing
        }
        catch (GeodatabaseGeneralException)
        {
          // The index passed was not within the valid range. // unclear reason of the error
        }
      }
    }
    return rowBuffer;
  }

  public List<FieldDescription> GetFieldsFromSpeckleLayer(GisLayer target)
  {
    if (target["attributes"] is Base attributes)
    {
      List<FieldDescription> fields = new();
      List<string> fieldAdded = new();

      foreach (var field in attributes.GetMembers(DynamicBaseMemberType.Dynamic))
      {
        if (!fieldAdded.Contains(field.Key) && field.Key != FID_FIELD_NAME)
        {
          // POC: TODO check for the forbidden characters/combinations: https://support.esri.com/en-us/knowledge-base/what-characters-should-not-be-used-in-arcgis-for-field--000005588
          try
          {
            if (field.Value is not null)
            {
              string key = field.Key;
              FieldType fieldType = GISAttributeFieldType.FieldTypeToNative(field.Value);

              FieldDescription fieldDescription =
                new(_characterCleaner.CleanCharacters(key), fieldType) { AliasName = key };
              fields.Add(fieldDescription);
              fieldAdded.Add(key);
            }
            else
            {
              // log missing field
            }
          }
          catch (GeodatabaseFieldException)
          {
            // log missing field
          }
        }
      }

      // every feature needs Speckle_ID to be colored (before we implement native GIS renderers on Receive)
      if (!fieldAdded.Contains("Speckle_ID"))
      {
        FieldDescription fieldDescriptionId =
          new(_characterCleaner.CleanCharacters("Speckle_ID"), FieldType.String) { AliasName = "Speckle_ID" };
        fields.Add(fieldDescriptionId);
      }

      return fields;
    }

    throw new ValidationException("Creation of the custom fields failed: provided object is not a valid Vector Layer");
  }

  public List<(FieldDescription, Func<Base, object?>)> CreateFieldsFromListOfBase(List<Base> target)
  {
    List<(FieldDescription, Func<Base, object?>)> fieldsAndFunctions = new();
    List<string> fieldAdded = new();

    foreach (var baseObj in target)
    {
      // get all members by default, but only Dynamic ones from the basic geometry
      Dictionary<string, object?> members = new();
      members["Speckle_ID"] = baseObj.id; // to use for unique color values

      // leave out until we decide which properties to support on Receive
      /*
      if (baseObj.speckle_type.StartsWith("Objects.Geometry"))
      {
        members = baseObj.GetMembers(DynamicBaseMemberType.Dynamic);
      }
      else
      {
        members = baseObj.GetMembers(DynamicBaseMemberType.All);
      }
      */

      foreach (KeyValuePair<string, object?> field in members)
      {
        // POC: TODO check for the forbidden characters/combinations: https://support.esri.com/en-us/knowledge-base/what-characters-should-not-be-used-in-arcgis-for-field--000005588
        string key = field.Key;
        if (field.Key == "Speckle_ID")
        {
          key = "id";
        }
        Func<Base, object?> function = x => x[key];
        TraverseAttributes(field, function, fieldsAndFunctions, fieldAdded);
      }
    }

    // change all FieldType.Blob to String
    // "Blob" will never be used on receive, so it is a placeholder for non-properly identified fields
    for (int i = 0; i < fieldsAndFunctions.Count; i++)
    {
      (FieldDescription description, Func<Base, object?> function) = fieldsAndFunctions[i];
      if (description.FieldType is FieldType.Blob)
      {
        fieldsAndFunctions[i] = new(
          new FieldDescription(description.Name, FieldType.String) { AliasName = description.AliasName },
          function
        );
      }
    }

    return fieldsAndFunctions;
  }

  private void TraverseAttributes(
    KeyValuePair<string, object?> field,
    Func<Base, object?> function,
    List<(FieldDescription, Func<Base, object?>)> fieldsAndFunctions,
    List<string> fieldAdded
  )
  {
    if (field.Value is Base attributeBase)
    {
      // Revit parameters are sent under the `properties` field as a `Dictionary<string,object?>`.
      // This is the same for attributes from other applications. No Speckle objects should have attributes of type `Base`.
      // Currently we are not sending any rhino user strings.
      // TODO: add support for attributes of type `Dictionary<string,object?>`
    }
    else if (field.Value is IList attributeList)
    {
      int count = 0;
      foreach (var attributField in attributeList)
      {
        KeyValuePair<string, object?> newAttributField = new($"{field.Key}[{count}]", attributField);
        Func<Base, object?> functionAdded = x => (function(x) as List<object?>)?[count];
        TraverseAttributes(newAttributField, functionAdded, fieldsAndFunctions, fieldAdded);
        count += 1;
      }
    }
    else
    {
      TryAddField(field, function, fieldsAndFunctions, fieldAdded);
    }
  }

  private void TryAddField(
    KeyValuePair<string, object?> field,
    Func<Base, object?> function,
    List<(FieldDescription, Func<Base, object?>)> fieldsAndFunctions,
    List<string> fieldAdded
  )
  {
    try
    {
      string key = field.Key;
      string cleanKey = _characterCleaner.CleanCharacters(key);

      if (cleanKey == FID_FIELD_NAME) // we cannot add field with reserved name
      {
        return;
      }

      if (!fieldAdded.Contains(cleanKey))
      {
        // use field.Value to define FieldType
        FieldType fieldType = GISAttributeFieldType.GetFieldTypeFromRawValue(field.Value);

        FieldDescription fieldDescription = new(cleanKey, fieldType) { AliasName = key };
        fieldsAndFunctions.Add((fieldDescription, function));
        fieldAdded.Add(cleanKey);
      }
      else
      {
        // if field exists, check field.Value again, and revise FieldType if needed
        int index = fieldsAndFunctions.TakeWhile(x => x.Item1.Name != cleanKey).Count();

        (FieldDescription, Func<Base, object?>) itemInList;
        try
        {
          itemInList = fieldsAndFunctions[index];
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          return;
        }

        FieldType existingFieldType = itemInList.Item1.FieldType;
        FieldType newFieldType = GISAttributeFieldType.GetFieldTypeFromRawValue(field.Value);

        // adjust FieldType if needed, default everything to Strings if fields types differ:
        // 1. change to NewType, if old type was undefined ("Blob")
        // 2. change to NewType if it's String (and the old one is not)
        if (
          newFieldType != FieldType.Blob && existingFieldType == FieldType.Blob
          || newFieldType == FieldType.String && existingFieldType != FieldType.String
        )
        {
          fieldsAndFunctions[index] = (
            new FieldDescription(itemInList.Item1.Name, newFieldType) { AliasName = itemInList.Item1.AliasName },
            itemInList.Item2
          );
        }
      }
    }
    catch (GeodatabaseFieldException)
    {
      // do nothing
    }
  }

  public List<(FieldDescription, Func<Base, object?>)> GetFieldsAndAttributeFunctions(
    List<(TraversalContext, ObjectConversionTracker)> listOfContextAndTrackers
  )
  {
    List<(FieldDescription, Func<Base, object?>)> fieldsAndFunctions = new();
    List<FieldDescription> fields = new();

    // Get Fields, geomType and attributeFunction - separately for GIS and non-GIS
    if (
      listOfContextAndTrackers.FirstOrDefault().Item1.Parent?.Current is SGIS.GisLayer vLayer
      && vLayer["attributes"] is Base
    ) // GIS
    {
      fields = GetFieldsFromSpeckleLayer(vLayer);
      fieldsAndFunctions = fields
        .Select(x => (x, (Func<Base, object?>)(y => (y?["properties"] as Base)?[x.Name])))
        .ToList();
    }
    else // non-GIS
    {
      fieldsAndFunctions = CreateFieldsFromListOfBase(listOfContextAndTrackers.Select(x => x.Item2.Base).ToList());
    }

    return fieldsAndFunctions;
  }
}
