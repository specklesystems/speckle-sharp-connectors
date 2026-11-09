using System.Diagnostics;
using Autodesk.ProcessPower.DataObjects;
using Autodesk.ProcessPower.PnIDObjects;
using Autodesk.ProcessPower.ProjectManager;
using Microsoft.Extensions.Logging;
using Speckle.Sdk;

namespace Speckle.Converters.Plant3dShared.ToSpeckle;

public sealed class Plant3dLineGroupResolver(ILogger<Plant3dLineGroupResolver> logger)
{
  private enum DocType
  {
    PnID,
    Piping,
    Other,
  };

  private sealed record PnpGroupConfig(string RelationshipTable, string GroupType, string PartType, string LineNumber);

  private readonly PnpGroupConfig _pipingConfig = new("P3dLineGroupPartRelationship", "LineGroup", "Part", "Number");
  private readonly PnpGroupConfig _pnidPipeConfig = new(
    "PipeLineGroupRelationship",
    "PipeLineGroup",
    "PipeLine",
    "LineNumber"
  );
  private readonly PnpGroupConfig _pnidSignalConfig = new(
    "SignalLineGroupRelationship",
    "SignalLineGroup",
    "SignalLine",
    "Number"
  );

  private static DocType GetDocumentType() =>
    Enum.TryParse<DocType>(PnPProjectUtils.GetActiveDocumentType(), true, out var docType) ? docType : DocType.Other;

  private object? GetRowValue(PnPRow row, string column)
  {
    if (row.Table.Columns.Contains(column))
    {
      return row[column];
    }
    else
    {
      logger.LogDebug("The PnP database table {table} does not contain column {column}", row.Table.Name, column);
      return null;
    }
  }

  private bool TryGetGroupInfo(
    PPDL.DataLinksManager dataLinksManager,
    int rowId,
    PnpGroupConfig config,
    out LineGroupInfo? groupInfo
  )
  {
    groupInfo = null;

    var groupId = dataLinksManager
      .GetRelatedRowIds(config.RelationshipTable, config.PartType, rowId, config.GroupType)
      .FirstOrDefault();
    if (groupId == 0)
    {
      return false;
    }

    var database = dataLinksManager.GetPnPDatabase();
    var groupRow = database.GetRow(groupId);
    if (groupRow is null)
    {
      logger.LogDebug("Failed to find group row for group ID {GroupId}", groupId);
      return false;
    }

    string lineNumber = GetRowValue(groupRow, config.LineNumber)?.ToString() ?? "";
    groupInfo = new LineGroupInfo(config.GroupType, groupId, lineNumber);
    return true;
  }

  /// <summary>
  /// Gets the group ID, line number and type for objects that participate in a line group.
  /// Supports 2D pipe line and signal line groups, and 3D piping line groups.
  /// </summary>
  public bool TryGetGroupInfo(
    PPDL.DataLinksManager dataLinksManager,
    ADB.Entity entity,
    int rowId,
    out LineGroupInfo? groupInfo
  )
  {
    groupInfo = null;

    if (rowId <= 0)
    {
      return false;
    }

    try
    {
      return GetDocumentType() switch
      {
        // Groups are only supported for LineSegments in PnID, but for Piping, any entity can be part of a group.
        DocType.PnID => entity is LineSegment
          && (
            TryGetGroupInfo(dataLinksManager, rowId, _pnidPipeConfig, out groupInfo)
            || TryGetGroupInfo(dataLinksManager, rowId, _pnidSignalConfig, out groupInfo)
          ),
        DocType.Piping => TryGetGroupInfo(dataLinksManager, rowId, _pipingConfig, out groupInfo),
        DocType.Other => false,
        _ => throw new UnreachableException(),
      };
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogWarning(ex, "Failed to read PnP group relationship for object {HandleValue}", entity.Handle.Value);
    }

    return false;
  }
}

public record LineGroupInfo(string GroupType, int GroupId, string LineNumber);
