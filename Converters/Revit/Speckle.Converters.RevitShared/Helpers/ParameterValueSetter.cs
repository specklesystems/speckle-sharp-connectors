using System.Text.RegularExpressions;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Services;
using Speckle.Core.Models;

namespace Speckle.Converters.RevitShared.Helpers;

public class ParameterValueSetter(ScalingServiceToHost scalingService)
{
  public void SetInstanceParameters(DB.Element revitElement, Base speckleElement, List<string>? exclusions = null)
  {
    if (
      speckleElement["parameters"] is not Base speckleParameters
      || speckleParameters.GetDynamicMemberNames().Count() == 0
    )
    {
      return;
    }

    if (
      speckleElement["phaseCreated"] is string phaseCreated
      && !string.IsNullOrEmpty(phaseCreated)
      && GetRevitPhase(revitElement.Document, phaseCreated) is DB.Phase revitPhaseCreated
    )
    {
      TrySetParam(revitElement, DB.BuiltInParameter.PHASE_CREATED, revitPhaseCreated);
    }

    if (
      speckleElement["phaseDemolished"] is string phaseDemolished
      && !string.IsNullOrEmpty(phaseDemolished)
      && GetRevitPhase(revitElement.Document, phaseDemolished) is DB.Phase revitPhaseDemolished
    )
    {
      TrySetParam(revitElement, DB.BuiltInParameter.PHASE_DEMOLISHED, revitPhaseDemolished);
    }

    // NOTE: we are using the ParametersMap here and not Parameters, as it's a much smaller list of stuff and
    // Parameters most likely contains extra (garbage) stuff that we don't need to set anyways
    // so it's a much faster conversion. If we find that's not the case, we might need to change it in the future
    IEnumerable<DB.Parameter>? revitParameters = null;
    if (exclusions == null)
    {
      revitParameters = revitElement.ParametersMap.Cast<DB.Parameter>().Where(x => x != null && !x.IsReadOnly);
    }
    else
    {
      revitParameters = revitElement.ParametersMap
        .Cast<DB.Parameter>()
        .Where(x => x != null && !x.IsReadOnly && !exclusions.Contains(GetParamInternalName(x)));
    }

    // Here we are creating two  dictionaries for faster lookup
    // one uses the BuiltInName / GUID the other the name as Key
    // we need both to support parameter set by Schema Builder, that might be generated with one or the other
    // Also, custom parameters that are not Shared, will have an INVALID BuiltInParameter name and no GUID, then we need to use their name
    var revitParameterById = revitParameters.ToDictionary(x => GetParamInternalName(x), x => x);
    var revitParameterByName = revitParameters.ToDictionary(x => x.Definition.Name, x => x);

    // speckleParameters is a Base
    // its member names will have for Key either a BuiltInName, GUID or Name of the parameter (depending onwhere it comes from)
    // and as value the full Parameter object, that might come from Revit or SchemaBuilder
    // We only loop params we can set and that actually exist on the revit element
    var filteredSpeckleParameters = speckleParameters
      .GetMembers()
      .Where(x => revitParameterById.ContainsKey(x.Key) || revitParameterByName.ContainsKey(x.Key));

    foreach (var spk in filteredSpeckleParameters)
    {
      if (spk.Value is not SOBR.Parameter sp || sp.isReadOnly || sp.value == null)
      {
        continue;
      }

      var rp = revitParameterById.ContainsKey(spk.Key) ? revitParameterById[spk.Key] : revitParameterByName[spk.Key];

      TrySetParam(rp, sp.value, sp.units, sp.applicationUnit);
    }
  }

  private void TrySetParam(DB.Parameter rp, object value, string units = "", string applicationUnit = "")
  {
    try
    {
      SetParam(rp, value, units, applicationUnit);
    }
    catch (Autodesk.Revit.Exceptions.ApplicationException)
    {
      // POC: setting parameters via the above method can throw several different exceptions. We don't want any of these failed parameters to stop conversion of the object because these parameters are typically unimportant. All important parameters have been moved to specific properties in the object model. We should log these to learn more about what specific failures are occuring
    }
    catch (SpeckleConversionException)
    {
      // same as above
    }
  }

  private void SetParam(DB.Parameter rp, object value, string units = "", string applicationUnit = "")
  {
    switch (rp.StorageType)
    {
      case DB.StorageType.Double:
        DB.ForgeTypeId unitTypeId;
        // This is meant for parameters that come from Revit
        // as they might use a lot more unit types that Speckle doesn't currently support
        if (!string.IsNullOrEmpty(applicationUnit))
        {
          unitTypeId = new(applicationUnit);
        }
        else if (scalingService.UnitsToNative(units) is DB.ForgeTypeId typeId)
        {
          unitTypeId = typeId;
        }
        else
        {
          unitTypeId = rp.GetUnitTypeId();
        }
        rp.Set(scalingService.ScaleToNative(Convert.ToDouble(value), unitTypeId));
        break;

      case DB.StorageType.Integer:
        if (value is string s)
        {
          if (s.ToLower() == "no")
          {
            value = 0;
          }
          else if (s.ToLower() == "yes")
          {
            value = 1;
          }
        }
        rp.Set(Convert.ToInt32(value));
        break;

      case DB.StorageType.String:
        string stringValue =
          Convert.ToString(value)
          ?? throw new SpeckleConversionException(
            $"Expected parameter value storage type to be string, but instead it was {value.GetType()}"
          );
        var temp = Regex.Replace(stringValue, "[^0-9a-zA-Z ]+", "");
        rp.Set(temp);
        break;
      default:
        break;
    }
  }

  private void TrySetParam(DB.Element elem, DB.BuiltInParameter bip, DB.Element value)
  {
    var param = elem.get_Parameter(bip);
    if (param != null && value != null && !param.IsReadOnly)
    {
      param.Set(value.Id);
    }
  }

  private void TrySetParam(DB.Element elem, DB.BuiltInParameter bip, bool value)
  {
    var param = elem.get_Parameter(bip);
    if (param != null && !param.IsReadOnly)
    {
      param.Set(value ? 1 : 0);
    }
  }

  //Shared parameters use a GUID to be uniquely identified
  //Other parameters use a BuiltInParameter enum
  private static string GetParamInternalName(DB.Parameter rp)
  {
    if (rp.IsShared)
    {
      return rp.GUID.ToString();
    }
    else
    {
      var def = rp.Definition as DB.InternalDefinition;
      if (def.NotNull().BuiltInParameter == DB.BuiltInParameter.INVALID)
      {
        return def.Name;
      }

      return def.BuiltInParameter.ToString();
    }
  }

  private DB.Phase? GetRevitPhase(DB.Document document, string phaseName)
  {
    using var collector = new DB.FilteredElementCollector(document);

    Dictionary<string, DB.Phase> phases = collector
      .OfCategory(DB.BuiltInCategory.OST_Phases)
      .ToDictionary(el => el.Name, el => (DB.Phase)el);

    return phases.TryGetValue(phaseName, out var phase) ? phase : null;
  }
}
