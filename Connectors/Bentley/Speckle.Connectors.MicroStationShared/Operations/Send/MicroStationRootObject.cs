namespace Speckle.Connectors.MicroStation.Operations.Send;

/// <summary>
/// One unit of conversion: a managed DGN element plus the occurrence that carries it.
/// The base (active) model is the occurrence with an identity transform and an empty tag; every
/// reference attachment contributes another occurrence with the composed attachment transform
/// (dgnextract's ModelOccurrence, ENG-8749). Object identity is per OCCURRENCE — the same element
/// handle reached through two attachments of one model interns as two objects
/// (<see cref="ApplicationId"/> = element id + occurrence tag).
/// </summary>
public sealed record MicroStationRootObject(
  MgdElement Element,
  string ApplicationId,
  string OccurrenceTag,
  string ContainerLabel,
  BG.DTransform3d? OccurrenceTransform
)
{
  public static MicroStationRootObject InActiveModel(MgdElement element) =>
    new(element, ((ulong)element.ElementId).ToString(), "", "", null);
}
