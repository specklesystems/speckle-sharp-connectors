#nullable disable
#pragma warning disable IDE0040

using System.ComponentModel;
using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.GUI.HTML;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using OS = System.Environment;

///////////////////////////////////////////////////////////////////////////////
//                                                                           //
// NOTE: Copy pasted from rhino inside, with minor adjustments               //
// RhinoInside.Revit.GH/Extensions/Grasshopper/Special/ValueSet.cs           //
//                                                                           //
///////////////////////////////////////////////////////////////////////////////

///////////////////////////////////////////////////////////////////////////////
//                                                                           //
// NOTE: At some point this code may end up in the Grasshopper code base.    //
//                                                                           //
///////////////////////////////////////////////////////////////////////////////

namespace Speckle.Connectors.GrasshopperShared.Components.BaseComponents;

/// <summary>
/// <see cref="IEqualityComparer{T}"/> implementation for <see cref="IGH_Goo"/> references.
/// </summary>
/// <remarks>
/// Support most of the Grasshopper built-in types, but some types are not comparable, see code below.
/// </remarks>
struct GooEqualityComparer : IEqualityComparer<IGH_Goo>
{
  static bool IsEquatable(Type type) =>
    type?.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEquatable<>)) == true;

  public static bool IsEquatable(IGH_Goo goo)
  {
    return IsEquatable(goo?.GetType()!)
      || goo is IGH_GeometricGoo
      || goo is IGH_QuickCast
      || goo is GH_StructurePath
      || goo is GH_Culture
      || goo is IComparable
      || goo?.ScriptVariable() is object obj && (IsEquatable(obj.GetType()) || obj is IComparable || obj is ValueType);
  }

  public readonly bool Equals(IGH_Goo x, IGH_Goo y)
  {
    if (ReferenceEquals(x, y))
    {
      return true;
    }

    if (x is null)
    {
      return false;
    }

    if (y is null)
    {
      return false;
    }

    // Compare at Goo level
    if (x.GetType() is Type typeX && y.GetType() is Type typeY && typeX == typeY)
    {
      if (IsEquatable(typeX))
      {
        dynamic dynX = x,
          dynY = y;
        return dynX.Equals(dynY);
      }

      if (x is IGH_GeometricGoo geoX && y is IGH_GeometricGoo geoY)
      {
        if (geoX.IsReferencedGeometry || geoY.IsReferencedGeometry)
        {
          return geoX.ReferenceID == geoY.ReferenceID;
        }
      }
      else
      {
        if (x is GH_StructurePath pathX && y is GH_StructurePath pathY)
        {
          return pathX.Value == pathY.Value;
        }

        if (x is GH_Culture cultureX && y is GH_Culture cultureY)
        {
          return cultureX.Value == cultureY.Value;
        }
      }

      if (x is IGH_QuickCast qcX && y is IGH_QuickCast qcY)
      {
        return qcX.QC_CompareTo(qcY) == 0;
      }

      if (x is IComparable cX && y is IComparable cY)
      {
        return cX.CompareTo(cY) == 0;
      }

      // Compare at ScriptVariable level
      if (x.ScriptVariable() is object objX && y.ScriptVariable() is object objY)
      {
        return ScriptVariableEquals(objX, objY);
      }
    }

    return false;
  }

  static bool ScriptVariableEquals(object x, object y)
  {
    if (ReferenceEquals(x, y))
    {
      return true;
    }

    if (x is null)
    {
      return false;
    }

    if (y is null)
    {
      return false;
    }

    var typeX = x.GetType();
    var typeY = y.GetType();

    if (typeX == typeY)
    {
      if (x is Rhino.Geometry.GeometryBase geometryX && y is Rhino.Geometry.GeometryBase geometryY)
      {
        return Rhino.Geometry.GeometryBase.GeometryEquals(geometryX, geometryY);
      }

      if (IsEquatable(typeX))
      {
        dynamic dynX = x,
          dynY = y;
        return dynX.Equals(dynY);
      }

      if (x is IComparable comparableX && y is IComparable comparableY)
      {
        return comparableX.CompareTo(comparableY) == 0;
      }

      if (x is ValueType valueX && y is ValueType valueY)
      {
        return valueX.Equals(valueY);
      }
    }

    return false;
  }

  readonly int IEqualityComparer<IGH_Goo>.GetHashCode(IGH_Goo goo)
  {
    if (goo is null)
    {
      return 0;
    }

    if (IsEquatable(goo.GetType()))
    {
      return goo.GetHashCode();
    }

    if (goo is IGH_GeometricGoo geo && geo.IsReferencedGeometry)
    {
      return geo.ReferenceID.GetHashCode();
    }

    if (goo is IGH_QuickCast qc)
    {
      return qc.QC_Hash();
    }

    if (goo is GH_StructurePath path)
    {
      return path.Value.GetHashCode();
    }

    if (goo is GH_Culture culture)
    {
      return culture.Value.LCID;
    }

    if (goo is IComparable comparableGoo)
    {
      return comparableGoo.GetHashCode();
    }

    if (goo.ScriptVariable() is object obj)
    {
      if (IsEquatable(obj.GetType()))
      {
        return obj.GetHashCode();
      }

      if (obj is IComparable comparable)
      {
        return comparable.GetHashCode();
      }

      if (obj is ValueType value)
      {
        return value.GetHashCode();
      }
    }

    return 0;
  }
}

interface IGH_ItemDescription
{
  //bool RenderPreview(Graphics graphics, Size size);
  Bitmap GetTypeIcon(Size size);
  string Name { get; }
  string Identity { get; }
  string Description { get; }
}

#pragma warning disable CA1033
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class ValueSet<T> : GH_PersistentParam<T>, IGH_InitCodeAware, IGH_StateAwareObject, IGH_PreviewObject
  where T : class, IGH_Goo
#pragma warning restore CA1033
{
  protected override Bitmap Icon => s_icon;
  static readonly Bitmap s_icon = BuildIcon();

  static Bitmap BuildIcon()
  {
    var bitmap = new Bitmap(24, 24);
    using (var graphics = Graphics.FromImage(bitmap))
    {
      var iconBounds = new RectangleF(0.0f, 0.0f, 24.0f, 24.0f);
      iconBounds.Inflate(-0.5f, -0.5f);

      using (var capsule = GH_Capsule.CreateCapsule(iconBounds, GH_Palette.Grey))
      {
        capsule.Render(graphics, false, false, false);
        ResizableAttributes.RenderCheckMark(graphics, iconBounds, Color.Black);
      }
    }

    return bitmap;
  }

  void IGH_InitCodeAware.SetInitCode(string code) => SearchPattern = code;

  protected ValueSet(string name, string nickname, string description, string category, string subcategory)
    : base(name, nickname, description, category, subcategory)
  {
    // This makes the parameter not turn orange when there is nothing selected.
    Optional = true;
  }

  public enum DataCulling
  {
    None = 0,
    Nulls = 1 << 0,
    Invalids = 1 << 1,
    Duplicates = 1 << 2,
    Empty = 1 << 31
  };

  /// <summary>
  /// Culling nulls by default to make it work as a <see cref="CheckBox"/>.
  /// </summary>
  const DataCulling DEFAULT_CULLING = DataCulling.Nulls;
  DataCulling _culling = DEFAULT_CULLING;
  public DataCulling Culling
  {
    get => _culling & CullingMask;
    set => _culling = value;
  }

  public virtual DataCulling CullingMask =>
    DataCulling.Nulls | DataCulling.Invalids | DataCulling.Duplicates | DataCulling.Empty;

  static readonly Dictionary<Type, Bitmap> s_typeIcons = new Dictionary<Type, Bitmap>();

  protected virtual Bitmap GetItemIcon(T value, Size size)
  {
    var type = value.GetType();
    if (s_typeIcons.TryGetValue(GetType(), out var icon))
    {
      return icon;
    }

    var bitmap = default(Bitmap);
    switch (value)
    {
      case IGH_ItemDescription item:
        bitmap = item.GetTypeIcon(size);
        break;
    }

    if (bitmap is null)
    {
      // Try with a parameter that has the same name.
      var typeName = value.TypeName;
      var location = value.GetType().Assembly.Location;
      var proxy = Instances
        .ComponentServer.ObjectProxies.Where(x =>
          typeof(IGH_Param).IsAssignableFrom(x.Type)
          && string.Equals(x.Desc.Name, typeName, StringComparison.OrdinalIgnoreCase)
          && string.Equals(x.Location, location, StringComparison.OrdinalIgnoreCase)
        )
        .OrderBy(x => !x.SDKCompliant)
        .ThenBy(x => x.Obsolete)
        .FirstOrDefault();

      bitmap = proxy?.Icon;
    }

    return s_typeIcons[type] = bitmap!;
  }

  protected virtual string GetItemName(T value)
  {
    switch (value)
    {
      case IGH_ItemDescription item:
        return item.Name;
#if RHINO_8
      case Rhinoceros.ModelData data:
        return data.DisplayName;
#endif
      case IGH_Goo goo:
        return goo.ToString();
    }

    return value.ToString();
  }

  protected virtual string GetItemIdentity(T value)
  {
    switch (value)
    {
      case IGH_ItemDescription item:
        return item.Identity;

#if RHINO_8
      case Rhinoceros.ModelContent content:
        return content.Id.HasValue ? $"{{{content.Id.ToString().Substring(0, 8)}…}}" : string.Empty;
#endif

      // case GH_Colour color:
      //   if (color.Value.IsNamedColor)
      //     return color.Value.Name;
      //   return GH_ColorRGBA.TryGetName(color.Value, out var name) ? name : string.Empty;

      case IGH_GeometricGoo geom:
        return geom.IsReferencedGeometry ? $"{{{geom.ReferenceID.ToString()[8..]}…}}" : string.Empty;

      case IGH_Goo goo:
        return string.Empty;
    }

    return string.Empty;
  }

  protected virtual string GetItemDescription(T value)
  {
    switch (value)
    {
      case IGH_ItemDescription item:
        return item.Description;

#if RHINO_8
      case Rhinoceros.Params.IGH_ModelContentData contentData:
        return contentData.IsReferencedData ? Rhino.RhinoDoc.ActiveDoc?.Name ?? "Untitled.3dm" : string.Empty;
#endif

      case IGH_GeometricGoo geom:
        return geom.IsReferencedGeometry ? Rhino.RhinoDoc.ActiveDoc?.Name ?? "Untitled.3dm" : string.Empty;

      case IGH_Goo goo:
        return string.Empty;
    }

    return string.Empty;
  }

  public string SearchPattern { get; set; } = string.Empty;

  private bool _autoSelectAllItemsItems;

  /// <summary>
  /// When enabled, all available items will be selected automatically and persistently.
  /// </summary>
  public bool AutoSelectAllItems
  {
    get => _autoSelectAllItemsItems;
    set
    {
      if (_autoSelectAllItemsItems == value)
      {
        return;
      }

      _autoSelectAllItemsItems = value;

      if (value && _listItems.Count > 0)
      {
        SelectAllItems();
        ResetPersistentData(_listItems.Select(x => x.Value), "Enable auto-select all items");
      }

      OnDisplayExpired(false);
    }
  }

  protected internal int LayoutLevel { get; set; } = 1;

  sealed class ListItem
  {
    public ListItem(T goo, string name, string identity, bool selected = false)
    {
      Value = goo;
      Name = ToSingleLine(name);
      Identity = ToSingleLine(identity);
      Selected = selected;
    }

    private static string ToSingleLine(string value)
    {
      if (value is null)
      {
        return null;
      }

      var lines = value.Split(new string[] { OS.NewLine }, StringSplitOptions.RemoveEmptyEntries);
      switch (lines.Length)
      {
        case 0:
          return string.Empty;
        case 1:
          return value;
        default:
          return $"{lines[0]} ⤶"; // Alternatives ⏎⮐
      }
    }

    public readonly T Value;
    public readonly string Name;
    public readonly string Identity;
    public bool Selected;
    public RectangleF BoxName;
  }

  private List<ListItem> _listItems = [];
  IEnumerable<ListItem> SelectedItems => _listItems.Where(x => x.Selected);

  public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
  {
    if (Kind == GH_ParamKind.floating || Kind == GH_ParamKind.input)
    {
      Menu_AppendInternaliseData(menu);
    }
  }

  protected virtual void Menu_AppendPreProcessParameter(ToolStripDropDown menu)
  {
    var detail = Menu_AppendItem(menu, "Layout");
    Menu_AppendItem(detail.DropDown, "List", (s, a) => Menu_LayoutLevel(1), true, LayoutLevel == 1);
    Menu_AppendItem(detail.DropDown, "Details", (s, a) => Menu_LayoutLevel(2), true, LayoutLevel == 2);
  }

  private void Menu_LayoutLevel(int value)
  {
    RecordUndoEvent("Set: Layout");

    LayoutLevel = value;

    OnObjectChanged(GH_ObjectEventType.Options);

    OnDisplayExpired(true);
  }

  private void Menu_Culling(DataCulling value)
  {
    RecordUndoEvent("Set: Culling");

    if (Culling.HasFlag(value))
    {
      Culling &= ~value;
    }
    else
    {
      Culling |= value;
    }

    OnObjectChanged(GH_ObjectEventType.Options);

    if (Kind == GH_ParamKind.output)
    {
      ExpireOwner();
    }

    ExpireSolution(true);
  }

  protected virtual void Menu_AppendPostProcessParameter(ToolStripDropDown menu)
  {
    var cull = Menu_AppendItem(menu, "Cull");

    cull.Checked = Culling != DataCulling.None;
    if (CullingMask.HasFlag(DataCulling.Nulls))
    {
      Menu_AppendItem(
        cull.DropDown,
        "Nulls",
        (s, a) => Menu_Culling(DataCulling.Nulls),
        true,
        Culling.HasFlag(DataCulling.Nulls)
      );
    }

    if (CullingMask.HasFlag(DataCulling.Invalids))
    {
      Menu_AppendItem(
        cull.DropDown,
        "Invalids",
        (s, a) => Menu_Culling(DataCulling.Invalids),
        true,
        Culling.HasFlag(DataCulling.Invalids)
      );
    }

    if (CullingMask.HasFlag(DataCulling.Duplicates))
    {
      Menu_AppendItem(
        cull.DropDown,
        "Duplicates",
        (s, a) => Menu_Culling(DataCulling.Duplicates),
        true,
        Culling.HasFlag(DataCulling.Duplicates)
      );
    }

    if (CullingMask.HasFlag(DataCulling.Empty))
    {
      Menu_AppendItem(
        cull.DropDown,
        "Empty",
        (s, a) => Menu_Culling(DataCulling.Empty),
        true,
        Culling.HasFlag(DataCulling.Empty)
      );
    }
  }

  protected override void Menu_AppendPromptOne(ToolStripDropDown menu) { }

  protected override void Menu_AppendPromptMore(ToolStripDropDown menu) { }

  protected override GH_GetterResult Prompt_Plural(ref List<T> values) => GH_GetterResult.cancel;

  protected override GH_GetterResult Prompt_Singular(ref T value) => GH_GetterResult.cancel;

  // NOTE: removed from AppendAdditionalMenuItems as clearing selection simple enough. Keeping here just in case
  // we want to bring it back
  protected override void Menu_AppendDestroyPersistent(ToolStripDropDown menu) =>
    Menu_AppendItem(menu, "Clear selection", Menu_DestroyPersistentData, PersistentDataCount > 0);

  private void Menu_DestroyPersistentData(object sender, EventArgs e)
  {
    if (PersistentDataCount == 0)
    {
      return;
    }

    foreach (var item in _listItems)
    {
      item.Selected = false;
    }

    ResetPersistentData(null, "Clear selection");
  }

  protected override void Menu_AppendInternaliseData(ToolStripDropDown menu)
  {
    // Disabled Invert selection and one-off select all according to Discord chat. These are easy enough
    // Select all also enabled through ctrl+a
    //Menu_AppendItem(menu, "Invert selection", Menu_InvertSelectionClicked, _listItems.Count != PersistentDataCount);
    //Menu_AppendItem(menu, "Select all", Menu_SelectAllClicked, _listItems.Count != PersistentDataCount);

    var alwaysSelectAllItem = Menu_AppendItem(
      menu,
      "Auto-select all items",
      Menu_AlwaysSelectAllClicked,
      true,
      _autoSelectAllItemsItems
    );
    alwaysSelectAllItem.ToolTipText = _autoSelectAllItemsItems
      ? "Currently auto-selecting all available items. Click to disable."
      : "Enable automatic selection of all available items. Will persist when new data is input.";

    Menu_AppendItem(menu, "Internalise selection", Menu_InternaliseDataClicked, SourceCount > 0);
  }

  /// <summary>
  /// Helper method that reduces code duplication to select all items
  /// </summary>
  private void SelectAllItems()
  {
    foreach (var item in _listItems)
    {
      item.Selected = true;
    }
  }

  private void Menu_InternaliseDataClicked(object sender, EventArgs e)
  {
    if (SourceCount == 0)
    {
      return;
    }

    RecordUndoEvent("Internalise selection");

    _listItems = SelectedItems.ToList();

    foreach (var param in Sources)
    {
      param.Recipients.Remove(this);
    }

    Sources.Clear();
    OnObjectChanged(GH_ObjectEventType.Sources);

    OnDisplayExpired(false);
  }

  protected override void Menu_AppendExtractParameter(ToolStripDropDown menu) { }

  protected void Menu_InvertSelectionClicked(object sender, EventArgs e)
  {
    foreach (var item in _listItems)
    {
      item.Selected = !item.Selected;
    }

    ResetPersistentData(SelectedItems.Select(x => x.Value), "Invert selection");
  }

  protected void Menu_SelectAllClicked(object sender, EventArgs e)
  {
    SelectAllItems();
    ResetPersistentData(_listItems.Select(x => x.Value), "Select all");
  }

  /// <summary>
  /// Event handler for auto-select all items menu item
  /// </summary>
  private void Menu_AlwaysSelectAllClicked(object sender, EventArgs e) =>
    AutoSelectAllItems = !_autoSelectAllItemsItems;

  sealed class ResizableAttributes : GH_ResizableAttributes<ValueSet<T>>
  {
    public override bool HasInputGrip => true;
    public override bool HasOutputGrip => true;
    public override bool AllowMessageBalloon => true;
    protected override Padding SizingBorders => new Padding(4, 6, 4, 6);
    protected override Size MinimumSize => new Size(50 + PaddingLeft, 25 + ItemHeight * 5);

    public ResizableAttributes(ValueSet<T> owner)
      : base(owner)
    {
      Bounds = new RectangleF(
        Bounds.Location,
        new SizeF(Math.Max(200 + PaddingLeft, MinimumSize.Width), Math.Max(Bounds.Height, MinimumSize.Height))
      );
    }

    protected override void Layout()
    {
      if (MaximumSize.Width < Bounds.Width || Bounds.Width < MinimumSize.Width)
      {
        Bounds = new RectangleF(
          Bounds.Location,
          new SizeF(Bounds.Width < MinimumSize.Width ? MinimumSize.Width : MaximumSize.Width, Bounds.Height)
        );
      }

      if (MaximumSize.Height < Bounds.Height || Bounds.Height < MinimumSize.Height)
      {
        Bounds = new RectangleF(
          Bounds.Location,
          new SizeF(Bounds.Width, Bounds.Height < MinimumSize.Height ? MinimumSize.Height : MaximumSize.Height)
        );
      }

      var itemBounds = new RectangleF(Bounds.X + 2, Bounds.Y + 20, Bounds.Width - 4, ItemHeight);

      for (int i = 0; i < Owner._listItems.Count; i++)
      {
        Owner._listItems[i].BoxName = itemBounds;
        itemBounds = new RectangleF(
          itemBounds.X,
          itemBounds.Y + itemBounds.Height,
          itemBounds.Width,
          itemBounds.Height
        );
      }

      base.Layout();
    }

    static readonly Font s_typeNameFont = GH_FontServer.NewFont(GH_FontServer.StandardAdjusted, FontStyle.Italic);
    const int CAPTION_HEIGHT = 20;
    const int FOOTNOTE_HEIGHT = 18;
    const int SCROLLER_WIDTH = 8;
    int ItemHeight => 2 + 16 * Owner.LayoutLevel;
    int PaddingLeft => Owner.IconDisplayMode == GH_IconDisplayMode.application ? 0 : 30;

    Rectangle AdjustedBounds => GH_Convert.ToRectangle(Bounds);

    Rectangle CaptionBounds =>
      new Rectangle(
        AdjustedBounds.X + 2 + PaddingLeft,
        AdjustedBounds.Y + 1,
        AdjustedBounds.Width - 4 - PaddingLeft,
        CAPTION_HEIGHT - 2
      );

    Rectangle FootnoteBounds =>
      new Rectangle(
        (int)Bounds.X + 2 + PaddingLeft,
        (int)Bounds.Bottom - FOOTNOTE_HEIGHT,
        (int)Bounds.Width - 4 - PaddingLeft,
        FOOTNOTE_HEIGHT
      );

    Rectangle ListBounds =>
      new Rectangle(
        AdjustedBounds.X + PaddingLeft,
        AdjustedBounds.Y + CAPTION_HEIGHT,
        AdjustedBounds.Width - PaddingLeft,
        AdjustedBounds.Height - CAPTION_HEIGHT - FOOTNOTE_HEIGHT
      );

    Rectangle ScrollerBounds
    {
      get
      {
        var total = Owner._listItems.Count * ItemHeight;
        if (total > 0)
        {
          var scrollerBounds = ListBounds;
          var factor = (float)scrollerBounds.Height / total;
          if (factor < 1.0)
          {
            var scrollSize = Math.Max(scrollerBounds.Height * factor, ItemHeight);
            var position = (scrollerBounds.Height - scrollSize) * _scrollRatio;
            return GH_Convert.ToRectangle(
              new RectangleF(
                scrollerBounds.Right - SCROLLER_WIDTH - 2,
                scrollerBounds.Top + position,
                SCROLLER_WIDTH,
                scrollSize
              )
            );
          }
        }

        return Rectangle.Empty;
      }
    }

    float _scrollRatio;

    enum ScrollMode
    {
      None,
      Scrolling,
      Panning
    }

    ScrollMode _activeScrollMode = ScrollMode.None;

    float _scrolling = float.NaN;
    float _scrollingY = float.NaN;

    int _lastItemIndex;

#pragma warning disable CA1502
    protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
#pragma warning restore
    {
      switch (channel)
      {
        case GH_CanvasChannel.Wires:
        {
          if (Owner.SourceCount > 0)
          {
            RenderIncomingWires(canvas.Painter, Owner.Sources, Owner.WireDisplay);
          }

          break;
        }
        case GH_CanvasChannel.Objects:
        {
          var bounds = Bounds;
          if (!canvas.Viewport.IsVisible(ref bounds, 10))
          {
            return;
          }

          var palette = GH_CapsuleRenderEngine.GetImpliedPalette(Owner);
          if (palette == GH_Palette.Normal && (Owner as IGH_PreviewObject)?.IsPreviewCapable != true)
          {
            palette = GH_Palette.Hidden;
          }

          using (var capsule = GH_Capsule.CreateCapsule(bounds, palette))
          {
            capsule.AddInputGrip(InputGrip.Y);
            capsule.AddOutputGrip(OutputGrip.Y);
            capsule.Render(graphics, Selected, Owner.Locked, (Owner as IGH_PreviewObject)?.Hidden != false);

            var iconBox = capsule.Box_Content;
            iconBox.X += 3;
            iconBox.Y += 2;

            iconBox.Width = 24;
            iconBox.Height -= 4;

            if (Owner.IconDisplayMode == GH_IconDisplayMode.icon && Owner.IconCapableUI)
            {
              var icon = Owner.Locked ? Owner.Icon_24x24_Locked : Owner.Icon_24x24;
              capsule.RenderEngine.RenderIcon(graphics, icon, iconBox, 1);

              if (Owner.Obsolete)
              {
                GH_GraphicsUtil.RenderObjectOverlay(graphics, Owner, iconBox);
              }
            }
            else if (Owner.IconDisplayMode == GH_IconDisplayMode.name)
            {
              using (
                var capsuleName = GH_Capsule.CreateTextCapsule(
                  iconBox,
                  iconBox,
                  GH_Palette.Black,
                  Owner.NickName,
                  GH_FontServer.LargeAdjusted,
                  GH_Orientation.vertical_center,
                  3,
                  6
                )
              )
              {
                capsuleName.Render(graphics, Selected, Owner.Locked, false);
              }
            }
          }

          var alpha = GH_Canvas.ZoomFadeLow;
          if (alpha > 0)
          {
            var layoutLevel = Owner.LayoutLevel;

            canvas.SetSmartTextRenderingHint();
            var style = GH_CapsuleRenderEngine.GetImpliedStyle(palette, this);
            var textColor = Color.FromArgb(alpha, style.Text);

            var captionColor = string.IsNullOrEmpty(Owner.SearchPattern)
              ? Color.FromArgb(alpha / 2, style.Text)
              : textColor;

            using (var nameFill = new SolidBrush(captionColor))
            {
              graphics.DrawString(
                string.IsNullOrEmpty(Owner.SearchPattern) ? "Search…" : Owner.SearchPattern,
                GH_FontServer.LargeAdjusted,
                nameFill,
                CaptionBounds,
                GH_TextRenderingConstants.StringFormat(StringAlignment.Center, StringAlignment.Near)
              );
            }

            {
              var listBounds = ListBounds;

              Brush alternateBrush;
              if (
                GH_Canvas.ZoomFadeMedium > 0 /*&& Owner.DataType == GH_ParamData.remote*/
              )
              {
                graphics.FillRectangle(Brushes.White, listBounds);
                alternateBrush = Brushes.WhiteSmoke;
              }
              else
              {
                alternateBrush = new SolidBrush(Color.FromArgb(70, style.Fill));
              }

              var worldClip = graphics.Clip;
              graphics.SetClip(listBounds, System.Drawing.Drawing2D.CombineMode.Intersect);

              var worldTransform = graphics.Transform;
              if (!ScrollerBounds.IsEmpty)
              {
                var scroll = -(Owner._listItems.Count * ItemHeight - listBounds.Height) * _scrollRatio;
                graphics.TranslateTransform(0.0f, scroll);
              }

              try
              {
                using (var nameBrush = new SolidBrush(textColor))
                {
                  using (var descriptionBrush = new SolidBrush(Color.FromArgb(GH_Canvas.ZoomFadeLow, 80, 80, 80)))
                  {
                    using (var secondaryBrush = new SolidBrush(Color.FromArgb(GH_Canvas.ZoomFadeMedium, Color.Gray)))
                    {
                      using (
                        var primaryFormat = new StringFormat(StringFormatFlags.NoWrap)
                        {
                          Trimming = StringTrimming.EllipsisCharacter,
                          LineAlignment = StringAlignment.Center
                        }
                      )
                      {
                        using (
                          var secondaryFormat = new StringFormat(StringFormatFlags.NoWrap)
                          {
                            Trimming = StringTrimming.EllipsisPath,
                            LineAlignment = StringAlignment.Center,
                            Alignment = StringAlignment.Far
                          }
                        )
                        {
                          var itemBounds = new RectangleF(listBounds.X, listBounds.Y, listBounds.Width, ItemHeight);

                          for (int index = 0; index < Owner._listItems.Count; ++index)
                          {
                            if (graphics.IsVisible(itemBounds))
                            {
                              var item = Owner._listItems[index];

                              if (index % 2 != 0)
                              {
                                graphics.FillRectangle(alternateBrush, itemBounds);
                              }

                              // Info
                              {
                                var infoBounds = new RectangleF(
                                  itemBounds.X + 22,
                                  itemBounds.Y + 1,
                                  itemBounds.Width - 22 - ScrollerBounds.Width,
                                  itemBounds.Height - 2
                                );
                                infoBounds.Width -= layoutLevel * 12 + 4;

                                if (GH_Canvas.ZoomFadeMedium > 0 && listBounds.Width > 250f)
                                {
                                  var secondaryInfoBounds = infoBounds;
                                  secondaryInfoBounds.Width -= 2;
                                  secondaryInfoBounds.Height /= layoutLevel;

                                  if (item.Identity is string identity && !string.IsNullOrEmpty(identity))
                                  {
                                    graphics.DrawString(
                                      identity,
                                      GH_FontServer.StandardAdjusted,
                                      secondaryBrush,
                                      secondaryInfoBounds,
                                      secondaryFormat
                                    );
                                  }

                                  if (layoutLevel > 1)
                                  {
                                    if (
                                      Owner.GetItemDescription(item.Value) is string itemDescription
                                      && !string.IsNullOrEmpty(itemDescription)
                                    )
                                    {
                                      var descriptionBounds = secondaryInfoBounds;
                                      descriptionBounds.Y += 16;
                                      graphics.DrawString(
                                        itemDescription,
                                        GH_FontServer.StandardAdjusted,
                                        secondaryBrush,
                                        descriptionBounds,
                                        secondaryFormat
                                      );
                                    }
                                  }
                                }
                              }

                              if (item.Selected)
                              {
                                if (
                                  GH_Canvas.ZoomFadeMedium > 0 /*&& Owner.DataType == GH_ParamData.remote*/
                                )
                                {
                                  var highlightBounds = itemBounds;
                                  highlightBounds.Inflate(-1, -1);
                                  GH_GraphicsUtil.RenderHighlightBox(
                                    graphics,
                                    GH_Convert.ToRectangle(highlightBounds),
                                    2,
                                    true,
                                    true
                                  );
                                }

                                var markBounds = new RectangleF(itemBounds.X + 1, itemBounds.Y, 22, itemBounds.Height);
                                RenderCheckMark(graphics, markBounds, textColor);
                              }

                              if (GH_Canvas.ZoomFadeMedium > 0 && listBounds.Width > 250f)
                              {
                                var iconSize = layoutLevel * 12;
                                var image =
                                  Owner.GetItemIcon(item.Value, new Size(iconSize, iconSize)) ?? s_defaultItemIcon;
                                var rect = new Rectangle(
                                  (int)(itemBounds.X + itemBounds.Width - 2 - iconSize - 2 - ScrollerBounds.Width),
                                  (int)(itemBounds.Y + ItemHeight / 2 - iconSize / 2),
                                  iconSize,
                                  iconSize
                                );

                                GH_GraphicsUtil.RenderFadedImage(graphics, image, rect, GH_Canvas.ZoomFadeMedium);
                              }

                              // Item
                              {
                                var itemClip = graphics.Clip;
                                graphics.SetClip(itemBounds, System.Drawing.Drawing2D.CombineMode.Intersect);

                                var itemTransform = graphics.Transform;
                                {
                                  graphics.TranslateTransform(itemBounds.X, itemBounds.Y);
                                  RenderItem(
                                    item,
                                    graphics,
                                    itemBounds.Size,
                                    (nameBrush, primaryFormat),
                                    (descriptionBrush, primaryFormat),
                                    layoutLevel
                                  );
                                }
                                graphics.Transform = itemTransform;
                                graphics.Clip = itemClip;
                              }
                            }

                            itemBounds.Y += itemBounds.Height;
                          }
                        }
                      }
                    }
                  }
                }
              }
              finally
              {
                graphics.Transform = worldTransform;
                graphics.Clip = worldClip;
              }

              RenderScrollBar(graphics, style.Text);

              if (
                GH_Canvas.ZoomFadeMedium > 0 /*&& Owner.DataType == GH_ParamData.remote*/
              )
              {
                using (var edge = new Pen(style.Edge))
                {
                  graphics.DrawRectangle(edge, listBounds.X, listBounds.Y, listBounds.Width, listBounds.Height);
                }

                GH_GraphicsUtil.ShadowHorizontal(graphics, listBounds.Left, listBounds.Right, listBounds.Top);
              }
              else
              {
                GH_GraphicsUtil.EtchFadingHorizontal(
                  graphics,
                  listBounds.Left,
                  listBounds.Right,
                  listBounds.Top,
                  (int)(0.8 * alpha),
                  (int)(0.3 * alpha)
                );
                GH_GraphicsUtil.EtchFadingHorizontal(
                  graphics,
                  listBounds.Left,
                  listBounds.Right,
                  listBounds.Bottom + 1,
                  (int)(0.8 * alpha),
                  (int)(0.3 * alpha)
                );
              }

              graphics.DrawString(
                $"{Owner._listItems.Count} items",
                GH_FontServer.StandardAdjusted,
                Brushes.Gray,
                FootnoteBounds,
                GH_TextRenderingConstants.FarCenter
              );
            }
          }

          return;
        }
      }

      base.Render(canvas, graphics, channel);
    }

    private static readonly Guid s_genericDataParamComponentGuid = new Guid("{8EC86459-BF01-4409-BAEE-174D0D2B13D0}");
    private static readonly Bitmap s_defaultItemIcon = Instances.ComponentServer.EmitObjectIcon(
      s_genericDataParamComponentGuid
    );

    private void RenderItem(
      ListItem item,
      Graphics graphics,
      SizeF itemSize,
      (Brush Brush, StringFormat Format) name,
      (Brush Brush, StringFormat Format) typeName,
      int layoutLevel
    )
    {
      var nameBounds = new RectangleF(22, 1, itemSize.Width - 22, (itemSize.Height - 2) / layoutLevel);

      // Name & NickName
      {
        string itemName = item.Name;

        if (itemName is null)
        {
          itemName = "<null>";
          name.Brush = Brushes.LightGray;
        }
        else if (itemName.Length == 0)
        {
          itemName = "<empty>";
          name.Brush = Brushes.LightGray;
        }

        graphics.DrawString(itemName, GH_FontServer.StandardAdjusted, name.Brush, nameBounds, name.Format);

        if (layoutLevel > 1)
        {
          var textBounds = new RectangleF
          {
            X = nameBounds.X,
            Y = nameBounds.Y + 16,
            Width = nameBounds.Width,
            Height = nameBounds.Height
          };

          graphics.DrawString(item.Value.TypeName, s_typeNameFont, typeName.Brush, textBounds, typeName.Format);
        }
      }
    }

    public static void RenderCheckMark(Graphics graphics, RectangleF bounds, Color color)
    {
      var x = (int)(bounds.X + 0.5F * bounds.Width) - 2;
      var y = (int)(bounds.Y + 0.5F * bounds.Height);
      var corners = new PointF[]
      {
        new PointF(x, y),
        new PointF(x - 3.5F, y - 3.5F),
        new PointF(x - 6.5F, y - 0.5F),
        new PointF(x, y + 6.0F),
        new PointF(x + 9.5F, y - 3.5F),
        new PointF(x + 6.5F, y - 6.5F)
      };

      using (var edge = new Pen(color, 1.0F))
      {
        edge.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
        graphics.FillPolygon(new SolidBrush(Color.FromArgb(150, color)), corners);
        graphics.DrawPolygon(edge, corners);
      }
    }

    private void RenderScrollBar(Graphics graphics, Color color)
    {
      var total = Owner._listItems.Count * ItemHeight;
      if (total > 0)
      {
        var scrollerBounds = ScrollerBounds;
        if (!scrollerBounds.IsEmpty)
        {
          using (
            var pen = new Pen(Color.FromArgb(Math.Min(GH_Canvas.ZoomFadeMedium, 100), color), SCROLLER_WIDTH)
            {
              StartCap = System.Drawing.Drawing2D.LineCap.Round,
              EndCap = System.Drawing.Drawing2D.LineCap.Round
            }
          )
          {
            var startPoint = new PointF(scrollerBounds.X + scrollerBounds.Width / 2, scrollerBounds.Top + 5);
            var endPoint = new PointF(scrollerBounds.X + scrollerBounds.Width / 2, scrollerBounds.Bottom - 5);

            graphics.DrawLine(pen, startPoint, endPoint);
          }
        }
      }
    }

    public override GH_ObjectResponse RespondToMouseDown(GH_Canvas canvas, GH_CanvasMouseEvent e)
    {
      if (canvas.Viewport.Zoom >= GH_Viewport.ZoomDefault * 0.6f)
      {
        var canvasLocation = new Point((int)e.CanvasLocation.X, (int)e.CanvasLocation.Y);

        if (e.Button == MouseButtons.Left)
        {
          var clientBounds = new RectangleF(
            Bounds.X + SizingBorders.Left,
            Bounds.Y + SizingBorders.Top,
            Bounds.Width - SizingBorders.Horizontal,
            Bounds.Height - SizingBorders.Vertical
          );

          if (clientBounds.Contains(e.CanvasLocation))
          {
            if (ScrollerBounds.Contains(canvasLocation))
            {
              _activeScrollMode = ScrollMode.Scrolling;
              _scrollingY = e.CanvasY;
              _scrolling = _scrollRatio;
              return GH_ObjectResponse.Capture;
            }
            else if (
              ListBounds.Contains(canvasLocation)
              && canvas.Viewport.Zoom >= GH_Viewport.ZoomDefault * 0.8f /*&& Owner.DataType == GH_ParamData.remote*/
            )
            {
              if (Owner._listItems.Count > 0)
              {
                var scrolledCanvasLocation = e.CanvasLocation;
                if (!ScrollerBounds.IsEmpty)
                {
                  scrolledCanvasLocation.Y += (Owner._listItems.Count * ItemHeight - ListBounds.Height) * _scrollRatio;
                }

                bool keepSelection = (Control.ModifierKeys & Keys.Control) != Keys.None;
                bool rangeSelection = (Control.ModifierKeys & Keys.Shift) != Keys.None;
                int lastItemIndex = 0;

                bool sel = _lastItemIndex < Owner._listItems.Count && Owner._listItems[_lastItemIndex].Selected;
                for (int i = 0; i < Owner._listItems.Count; i++)
                {
                  if (Owner._listItems[i].BoxName.Contains(scrolledCanvasLocation))
                  {
                    Owner._listItems[i].Selected ^= true;
                    lastItemIndex = i;
                  }
                  else if (!keepSelection)
                  {
                    Owner._listItems[i].Selected = false;
                  }
                }

                if (rangeSelection)
                {
                  int min = Math.Min(lastItemIndex, _lastItemIndex);
                  int max = Math.Max(lastItemIndex, _lastItemIndex);

                  for (int i = min; i <= max; i++)
                  {
                    Owner._listItems[i].Selected = sel;
                  }
                }

                _lastItemIndex = lastItemIndex;
                Owner.ResetPersistentData(Owner.SelectedItems.Select(x => x.Value), "Change selection");
              }

              return GH_ObjectResponse.Handled;
            }
          }
        }

        if (e.Button == MouseButtons.Right)
        {
          if (ListBounds.Contains(canvasLocation) && Control.ModifierKeys.HasFlag(Keys.Shift))
          {
            _activeScrollMode = ScrollMode.Panning;
            Instances.CursorServer.AttachCursor(canvas, "GH_HandPan");
            _scrollingY = e.CanvasY;
            _scrolling = _scrollRatio;
            return GH_ObjectResponse.Capture;
          }
        }
      }

      return base.RespondToMouseDown(canvas, e);
    }

    public override GH_ObjectResponse RespondToMouseUp(GH_Canvas canvas, GH_CanvasMouseEvent e)
    {
      if (_activeScrollMode != ScrollMode.None)
      {
        _activeScrollMode = ScrollMode.None;
        Instances.CursorServer.ResetCursor(canvas);
        _scrolling = float.NaN;
        _scrollingY = float.NaN;
        return GH_ObjectResponse.Release;
      }

      return base.RespondToMouseUp(canvas, e);
    }

    public override GH_ObjectResponse RespondToMouseMove(GH_Canvas canvas, GH_CanvasMouseEvent e)
    {
      if (_activeScrollMode != ScrollMode.None)
      {
        if (!float.IsNaN(_scrolling))
        {
          var dy = e.CanvasY - _scrollingY;
          var ty =
            _activeScrollMode == ScrollMode.Scrolling
              ? ListBounds.Height - ScrollerBounds.Height
              : -(Owner._listItems.Count * ItemHeight - ListBounds.Height);
          var f = dy / ty;

          _scrollRatio = Math.Max(0.0f, Math.Min(_scrolling + f, 1.0f));

          ExpireLayout();
          canvas.Refresh();
        }

        return GH_ObjectResponse.Handled;
      }

      return base.RespondToMouseMove(canvas, e);
    }

    public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas canvas, GH_CanvasMouseEvent e)
    {
      if (canvas.Viewport.Zoom >= GH_Viewport.ZoomDefault * 0.6f)
      {
        if (e.Button == MouseButtons.Left)
        {
          var clientBounds = new RectangleF(
            Bounds.X + SizingBorders.Left,
            Bounds.Y + SizingBorders.Top,
            Bounds.Width - SizingBorders.Horizontal,
            Bounds.Height - SizingBorders.Vertical
          );
          if (clientBounds.Contains(e.CanvasLocation))
          {
            var canvasLocation = new Point((int)e.CanvasLocation.X, (int)e.CanvasLocation.Y);
            if (CaptionBounds.Contains(canvasLocation))
            {
              var pattern = new SearchInputBox(this);
              pattern.ShowTextInputBox(
                canvas,
                Owner.SearchPattern,
                true,
                true,
                canvas.Viewport.XFormMatrix(GH_Viewport.GH_DisplayMatrix.CanvasToControl)
              );

              return GH_ObjectResponse.Handled;
            }
          }
        }
      }

      return GH_ObjectResponse.Ignore;
    }

    private sealed class SearchInputBox : Grasshopper.GUI.Base.GH_TextBoxInputBase
    {
      private readonly ResizableAttributes _parentAttributes;

      public SearchInputBox(ResizableAttributes parentAttributes)
      {
        _parentAttributes = parentAttributes;
        var bounds = parentAttributes.CaptionBounds;
        bounds.X += 2;
        bounds.Width -= 4;
        bounds.Y += 1;
        bounds.Height -= 2;

        Bounds = bounds;
        Font = GH_FontServer.StandardAdjusted;
      }

      protected override void HandleTextInputAccepted(string text)
      {
        _parentAttributes._scrollRatio = 0.0f;

        if (_parentAttributes.Owner.SearchPattern == text)
        {
          _parentAttributes.Owner.OnDisplayExpired(true);
          return;
        }

        _parentAttributes.Owner.RecordUndoEvent("Set Search Pattern");
        _parentAttributes.Owner.SearchPattern = text;
        _parentAttributes.Owner.OnObjectChanged(GH_ObjectEventType.Custom);

        _parentAttributes.Owner.ExpireSolution(true);
      }
    }
  }

  public override void CreateAttributes() => m_attributes = new ResizableAttributes(this);

  public override bool Read(GH_IReader reader)
  {
    if (!base.Read(reader))
    {
      return false;
    }

    string searchPattern = string.Empty;
    reader.TryGetString("SearchPattern", ref searchPattern);
    SearchPattern = searchPattern;

    int culling = (int)DEFAULT_CULLING;
    reader.TryGetInt32("Culling", ref culling);
    Culling = (DataCulling)culling;

    int layoutLevel = 1;
    reader.TryGetInt32("LayoutLevel", ref layoutLevel);
    LayoutLevel = Rhino.RhinoMath.Clamp(layoutLevel, 1, 2);

    bool alwaysSelectAll = false;
    if (reader.TryGetBoolean("AutoSelectAllItems", ref alwaysSelectAll))
    {
      _autoSelectAllItemsItems = alwaysSelectAll;
    }

    return true;
  }

  public override bool Write(GH_IWriter writer)
  {
    if (!base.Write(writer))
    {
      return false;
    }

    if (!string.IsNullOrEmpty(SearchPattern))
    {
      writer.SetString("SearchPattern", SearchPattern);
    }

    if (Culling != DEFAULT_CULLING)
    {
      writer.SetInt32("Culling", (int)Culling);
    }

    if (LayoutLevel != 1)
    {
      writer.SetInt32("LayoutLevel", LayoutLevel);
    }

    writer.SetBoolean("AutoSelectAllItems", _autoSelectAllItemsItems);

    return true;
  }

  public override void ClearData()
  {
    base.ClearData();

    foreach (var goo in PersistentData)
    {
#if RHINO8_OR_GREATER
      if (goo is IGH_ReferencedData id)
      {
        id.UnloadReferencedData();
        continue;
      }
#endif
      if (goo is IGH_GeometricGoo geo)
      {
        geo.ClearCaches();
      }
    }

    _listItems.Clear();
  }

  protected void ResetPersistentData(IEnumerable<T> list, string name)
  {
    RecordPersistentDataEvent(name);

    PersistentData.Clear();
    if (list is object)
    {
      PersistentData.AppendRange(list.Select(x => x.Duplicate() as T));
    }

    OnObjectChanged(GH_ObjectEventType.PersistentData);

    ExpireSolution(true);
  }

  protected virtual void LoadVolatileData()
  {
    if (DataType != GH_ParamData.local)
    {
      return;
    }

    foreach (var branch in m_data.Branches)
    {
      for (int i = 0; i < branch.Count; i++)
      {
        var goo = branch[i];

#if RHINO8_OR_GREATER
        if (
          goo is IGH_ReferencedData id
          && id.IsReferencedData
          && !id.IsReferencedDataLoaded
          && !id.LoadReferencedData()
        )
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"A referenced {goo.TypeName} could not be found.");
          continue;
        }
#endif
        if (goo is IGH_GeometricGoo geo && geo.IsReferencedGeometry && !geo.IsGeometryLoaded && !geo.LoadGeometry())
        {
          AddRuntimeMessage(
            GH_RuntimeMessageLevel.Warning,
            $"A referenced {geo.TypeName} could not be found in the Rhino document."
          );
        }
      }
    }
  }

  protected virtual void PreProcessVolatileData() { }

  protected virtual void ProcessVolatileData()
  {
    int nonComparableCount = 0;
    var volatileData = new HashSet<T>(
      VolatileData
        .AllData(true)
        .Where(x =>
        {
          if (GooEqualityComparer.IsEquatable(x))
          {
            return true;
          }

          nonComparableCount++;
          return false;
        })
        .Cast<T>(),
      new GooEqualityComparer()
    );

    if (nonComparableCount > 0)
    {
      // AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{nonComparableCount} non comparable elements filtered.");
    }

    var persistentData = new HashSet<IGH_Goo>(
      PersistentData.Where(x => GooEqualityComparer.IsEquatable(x)),
      new GooEqualityComparer()
    );

    var items = volatileData.Select(goo => new ListItem(
      goo,
      GetItemName(goo),
      GetItemIdentity(goo),
      persistentData.Contains(goo)
    ));

    // apply search
    if (!string.IsNullOrWhiteSpace(SearchPattern))
    {
      if (Operator.CompareMethodFromPattern(SearchPattern) != Operator.CompareMethod.Equals)
      {
        items = items.Where(x =>
          string.IsNullOrEmpty(SearchPattern) || Operator.IsSymbolNameLike(x.Name, SearchPattern)
        );
      }
    }

    _listItems = items.ToList();

    // Cull items that are not selected
    var selectedItems = new HashSet<IGH_Goo>(SelectedItems.Select(x => x.Value), new GooEqualityComparer());

    var pathCount = m_data.PathCount;
    for (int p = 0; p < pathCount; ++p)
    {
      var path = m_data.get_Path(p);
      var srcBranch = m_data.get_Branch(path);

      var itemCount = srcBranch.Count;
      for (int i = 0; i < itemCount; ++i)
      {
        if (!selectedItems.Contains((IGH_Goo)srcBranch[i]))
        {
          srcBranch[i] = default;
        }
      }
    }
  }

  protected virtual void PostProcessVolatileData() => base.PostProcessData();

  protected override void OnVolatileDataCollected()
  {
    base.OnVolatileDataCollected();

    if (Culling != DataCulling.None)
    {
      var data = new GH_Structure<T>();
      var pathCount = m_data.PathCount;
      for (int p = 0; p < pathCount; ++p)
      {
        var path = m_data.Paths[p];
        var branch = m_data.get_Branch(path);

        var items = branch.Cast<IGH_Goo>();
        if (Culling.HasFlag(DataCulling.Nulls))
        {
          items = items.Where(x => x is object);
        }

        if (Culling.HasFlag(DataCulling.Invalids))
        {
          items = items.Where(x => x?.IsValid != false);
        }

        if (Culling.HasFlag(DataCulling.Duplicates))
        {
          items = items.Distinct(new GooEqualityComparer());
        }

        if (!Culling.HasFlag(DataCulling.Empty) || items.Any())
        {
          data.AppendRange(items.Cast<T>(), path);
        }
      }

      m_data = data;
    }
  }

  private static (int Match, double Ratio) FuzzyPartialMatch(string key, string value)
  {
    var shortText = key.Length <= value.Length ? key : value;
    var longText = value.Length >= key.Length ? value : key;

    var maxMatch = int.MinValue;
    for (int i = 0; i < longText.Length - shortText.Length + 1; ++i)
    {
      var match =
        shortText.Length - GH_StringMatcher.LevenshteinDistance(shortText, longText.Substring(i, shortText.Length));
      if (match > maxMatch)
      {
        maxMatch = match;
      }
    }

    return (maxMatch, maxMatch / (double)longText.Length);
  }

  private static (int Match, double TokenRatio) FuzzyTokenMatch(string key, string value)
  {
    var keys = key.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    var values = value.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

    (int match, double ratio) = (0, 0.0);
    for (int k = 0; k < keys.Length; ++k)
    {
      (int match, double ratio) score = (0, 0.0);
      for (int v = 0; v < values.Length; ++v)
      {
        var partial = FuzzyPartialMatch(keys[k], values[v]);
        if (partial.CompareTo(score) > 0)
        {
          score = partial;
        }
      }

      match += score.match;
      ratio += score.ratio;
    }

    return (match, ratio / keys.Length);
  }

  private static (double Match, double TokenRatio, double Ratio) FuzzyTokenMatchRatio(
    string key,
    string value,
    bool caseSensitive
  )
  {
    var score = FuzzyTokenMatch(key, value);
    if (caseSensitive)
    {
      return (score.Match, score.TokenRatio, score.Match / value.Length);
    }

    var scoreInvariant = FuzzyTokenMatch(key.ToLowerInvariant(), value.ToLowerInvariant());
    score.Match += scoreInvariant.Match;
    score.TokenRatio += scoreInvariant.TokenRatio;

    return (score.Match * 0.5, score.TokenRatio * 0.5, score.Match / (double)value.Length);
  }

  protected virtual void SortItems()
  {
    // Show elements sorted Alphabetically.
    _listItems = _listItems.OrderBy(x => x.Name).ThenBy(x => x.Identity).ToList();
  }

  public sealed override void PostProcessData()
  {
    LoadVolatileData();
    PreProcessVolatileData();
    ProcessVolatileData();
    SortItems();

    // Order by fuzzy token if suits.
    if (!string.IsNullOrEmpty(SearchPattern))
    {
      _listItems = _listItems
        .OrderByDescending(x => FuzzyTokenMatchRatio(SearchPattern, x.Name ?? string.Empty, false))
        .ThenBy(x => x.Identity)
        .ToList();
    }

    PostProcessVolatileData();

    // auto-select if enabled
    if (_autoSelectAllItemsItems && _listItems.Count > 0 && _listItems.Any(item => !item.Selected))
    {
      SelectAllItems();
      ResetPersistentData(_listItems.Select(x => x.Value), null);
    }
  }

  public override void RegisterRemoteIDs(GH_GuidTable id_list)
  {
    foreach (var item in SelectedItems)
    {
      if (item.Value is IGH_GeometricGoo geo)
      {
        id_list.Add(geo.ReferenceID, this);
      }
    }
  }

  protected override string HtmlHelp_Source()
  {
    var nTopic = new GH_HtmlFormatter(this)
    {
      Title = Name,
      Description =
        @"<p>This component is a special interface object that allows for quick picking an item from a list.</p>"
        + @"<p>Double click on it and use the name input box to enter an exact name, alternativelly you can enter a name pattern. "
        + @"If a pattern is used, this param list will be filled up with all the items that match it.</p>"
        + @"<p>Several kind of patterns are supported, the method used depends on the first pattern character:</p>"
        + @"<dl>"
        + @"<dt><b><</b></dt><dd>Starts with</dd>"
        + @"<dt><b>></b></dt><dd>Ends with</dd>"
        + @"<dt><b>?</b></dt><dd>Contains, same as a regular search</dd>"
        + @"<dt><b>:</b></dt><dd>Wildcards, see Microsoft.VisualBasic "
        + "<a target=\"_blank\" href=\"https://docs.microsoft.com/en-us/dotnet/visual-basic/language-reference/operators/like-operator#pattern-options\">LikeOperator</a></dd>"
        + @"<dt><b>;</b></dt><dd>Regular expresion, see "
        + "<a target=\"_blank\" href=\"https://docs.microsoft.com/en-us/dotnet/standard/base-types/regular-expression-language-quick-reference\">here</a> as reference</dd>"
        + @"</dl>"
    };

    return nTopic.HtmlFormat();
  }

  #region IGH_StateAwareObject
  string IGH_StateAwareObject.SaveState()
  {
    if (string.IsNullOrEmpty(SearchPattern) && PersistentData.IsEmpty)
    {
      return string.Empty;
    }

    var chunk = new GH_LooseChunk("ValueSet");

    chunk.SetString(nameof(SearchPattern), SearchPattern);
    PersistentData.Write(chunk.CreateChunk(nameof(PersistentData)));

    return chunk.Serialize_Xml();
  }

  void IGH_StateAwareObject.LoadState(string state)
  {
    if (!string.IsNullOrEmpty(state))
    {
      try
      {
        var chunk = new GH_LooseChunk("ValueSet");
        chunk.Deserialize_Xml(state);

        SearchPattern = chunk.GetString(nameof(SearchPattern));
        PersistentData.Read(chunk.FindChunk(nameof(PersistentData)));

        ExpireSolution(false);
        return;
      }
#pragma warning disable CA1031
      catch
#pragma warning restore
      {
        // ignored
      }
    }

    SearchPattern = string.Empty;
    PersistentData.Clear();
  }
  #endregion

  #region IGH_PreviewObject
  bool IGH_PreviewObject.Hidden { get; set; }
  bool IGH_PreviewObject.IsPreviewCapable => true;
  Rhino.Geometry.BoundingBox IGH_PreviewObject.ClippingBox => Preview_ComputeClippingBox();

  void IGH_PreviewObject.DrawViewportMeshes(IGH_PreviewArgs args) => Preview_DrawMeshes(args);

  void IGH_PreviewObject.DrawViewportWires(IGH_PreviewArgs args) => Preview_DrawWires(args);
  #endregion
}

#pragma warning restore IDE0040
