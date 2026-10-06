using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Autodesk.ProcessPower.PnIDObjects;
using Microsoft.Extensions.Logging;

namespace Speckle.Converters.Plant3dShared.ToSpeckle;

public sealed class Plant3dLineGroupResolver(ILogger<Plant3dLineGroupResolver> logger) : IDisposable
{
  private LineGroupManagerContext? _context;
  private InitState _initState = InitState.Uninitialized;

  private bool Initialize(PPDL.DataLinksManager dataLinksManager)
  {
    // Initialize and validate the LineGroupManager by calling a method that will throw if not available
    var context = new LineGroupManagerContext(dataLinksManager);
    if (Validate(context))
    {
      _context = context;
      _initState = InitState.Success;
      return true;
    }

    context.Dispose();
    _initState = InitState.Failed;
    return false;
  }

  private bool Validate(LineGroupManagerContext context)
  {
    try
    {
      context.Validate();
      return true;
    }
    catch (Autodesk.AutoCAD.Runtime.Exception ex)
    {
      logger.LogWarning(ex, "Failed to validate the LineGroupManager (2D drawings only)");
      return false;
    }
  }

  [MemberNotNullWhen(true, nameof(_context))]
  private bool EnsureInitialized(PPDL.DataLinksManager dataLinksManager) =>
    _initState switch
    {
      InitState.Success => true,
      InitState.Failed => false,
      InitState.Uninitialized => Initialize(dataLinksManager),
      InitState.Disposed => throw new ObjectDisposedException(nameof(Plant3dLineGroupResolver)),
      _ => throw new UnreachableException(),
    };

  /// <summary>
  /// Gets the group ID and Type for objects that participate in a line group.
  /// The dataLinksManager is used to initialize the LineGroupManager the first time this method is called.
  /// Subsequent calls will use the cached LineGroupManager.
  /// </summary>
  public bool TryGetGroupInfo(
    PPDL.DataLinksManager dataLinksManager,
    ADB.ObjectId objectId,
    out LineGroupInfo? groupInfo
  )
  {
    try
    {
      if (EnsureInitialized(dataLinksManager))
      {
        var groupId = _context.LineGroupManager.GroupId(objectId);
        if (groupId > 0)
        {
          var groupType = _context.LineGroupManager.Type(groupId).ToString();
          groupInfo = new LineGroupInfo(groupId, groupType);
          return true;
        }
      }
    }
    // The call to GroupId will raise an exception with the message "eNotImplementedYet" if the object can not participate in a group.
    catch (Autodesk.AutoCAD.Runtime.Exception) { }

    groupInfo = null;
    return false;
  }

  public void Dispose()
  {
    if (_initState is not InitState.Disposed)
    {
      _context?.Dispose();
      _initState = InitState.Disposed;
    }
  }

  private enum InitState
  {
    Uninitialized,
    Success,
    Failed,
    Disposed,
  }

  /// <summary>
  /// Provides access to a LineGroupManager with validation and disposal ownership.
  /// </summary>
  private sealed class LineGroupManagerContext(PPDL.DataLinksManager dataLinksManager) : IDisposable
  {
    public LineGroupManager LineGroupManager { get; } = new LineGroupManager(dataLinksManager);

    /// <summary>
    /// Validates the LineGroupManager by calling a method that will throw if not available (e.g., in 3D drawings).
    /// </summary>
    /// <exception cref="Autodesk.AutoCAD.Runtime.Exception"></exception>
    public void Validate() => LineGroupManager.GroupIds(GroupType.PipeLineGroup);

    public void Dispose() => LineGroupManager.Dispose();
  }
}

public record LineGroupInfo(int GroupId, string GroupType);
