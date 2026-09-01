using Microsoft.Extensions.Logging;

namespace Speckle.Connectors.MicroStation.Operations.Send;

/// <summary>
/// Turns the send filter's element ids into occurrence-tagged <see cref="MicroStationRootObject"/>s,
/// and — when the "Include reference attachments" setting is on — walks the attachment tree
/// (dgnextract's ModelResolver::collectAttachments): one occurrence per ATTACHMENT, each carrying
/// the composed <see cref="DPN.DgnAttachment.GetTransformToParent"/> that lands its elements in the
/// master frame. A model attached at N placements is gathered N times. Cycle-guarded along the
/// recursion path, depth ≤ 16, hard cap of 4096 occurrences.
/// </summary>
public class MicroStationElementGatherer(ILogger<MicroStationElementGatherer> logger)
{
  private const int MAX_DEPTH = 16;
  private const int MAX_OCCURRENCES = 4096;

  public List<MicroStationRootObject> Gather(
    DPN.DgnModel activeModel,
    IReadOnlyCollection<string> selectedIds,
    bool includeReferences
  )
  {
    var result = new List<MicroStationRootObject>();

    // Base model occurrence: only the filter-selected elements.
    var idSet = new HashSet<string>(selectedIds);
    foreach (MgdElement? element in activeModel.GetGraphicElements())
    {
      if (element != null && idSet.Contains(((ulong)element.ElementId).ToString()))
      {
        result.Add(MicroStationRootObject.InActiveModel(element));
      }
    }

    if (includeReferences)
    {
      int occurrenceCount = 1;
      var onPath = new HashSet<string>();
      CollectAttachments(activeModel, null, "", result, onPath, 0, ref occurrenceCount);
    }

    return result;
  }

  private void CollectAttachments(
    DPN.DgnModelRef parent,
    BG.DTransform3d? parentTransform,
    string tag,
    List<MicroStationRootObject> result,
    HashSet<string> onPath,
    int depth,
    ref int occurrenceCount
  )
  {
    if (depth >= MAX_DEPTH)
    {
      return;
    }
    DPN.DgnAttachmentCollection? attachments;
    try
    {
      attachments = parent.GetDgnAttachments();
    }
    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
    {
      logger.LogWarning(ex, "Reference walk failed enumerating attachments.");
      return;
    }
    if (attachments == null)
    {
      return;
    }

    int n = 0;
    foreach (DPN.DgnAttachment? attachment in attachments)
    {
      if (attachment == null)
      {
        continue;
      }
      try
      {
        if (attachment.IsMissingFile || attachment.IsMissingModel || !attachment.IsDisplayed)
        {
          continue;
        }

        string pathKey = $"{attachment.AttachFileName}|{attachment.AttachModelName}";
        if (onPath.Contains(pathKey))
        {
          logger.LogWarning("Reference cycle at '{Model}'; branch stopped.", attachment.AttachModelName);
          continue;
        }
        if (occurrenceCount >= MAX_OCCURRENCES)
        {
          logger.LogWarning("Reference walk hit the {Cap}-placement cap; tree truncated.", MAX_OCCURRENCES);
          return;
        }

        DPN.DgnModel? model = attachment.GetDgnModel();
        if (model == null)
        {
          continue;
        }
        // Reference models are lazily loaded — make sure the graphics section is filled.
        model.FillSections(DPN.DgnModelSections.GraphicElements);

        attachment.GetTransformToParent(out BG.DTransform3d toParent, true);
        BG.DTransform3d composed = parentTransform is BG.DTransform3d p
          ? BG.DTransform3d.Multiply(p, toParent)
          : toParent;

        string childTag = tag + (tag.Length == 0 ? "@" : ".") + n.ToString();
        n++;
        occurrenceCount++;

        string label = attachment.AttachModelName ?? model.ModelName ?? childTag;
        string logical = attachment.AttachDescription ?? "";
        if (logical.Length > 0)
        {
          label += $" [{logical}]";
        }

        foreach (MgdElement? element in model.GetGraphicElements())
        {
          if (element != null)
          {
            result.Add(
              new MicroStationRootObject(
                element,
                ((ulong)element.ElementId).ToString() + childTag,
                childTag,
                label,
                composed
              )
            );
          }
        }

        onPath.Add(pathKey);
        CollectAttachments(model, composed, childTag, result, onPath, depth + 1, ref occurrenceCount);
        onPath.Remove(pathKey);
      }
      catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
      {
        logger.LogWarning(ex, "Reference attachment '{Model}' skipped.", attachment.AttachModelName);
      }
    }
  }
}
