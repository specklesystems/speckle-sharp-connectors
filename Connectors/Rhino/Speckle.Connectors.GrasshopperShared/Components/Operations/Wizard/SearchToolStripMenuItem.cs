namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Wizard;

public class SearchToolStripMenuItem
{
  private const string SEARCH_PLACEHOLDER_TEXT = "\uD83D\uDD0D Search...";
  private readonly TimeSpan _debounce = TimeSpan.FromMilliseconds(500);
  private DateTime _lastTime = DateTime.UtcNow;

  private ToolStripControlHost SearchHost { get; set; }
  private ToolStripDropDown ParentDropDown { get; set; }

  private string SearchItemId { get; set; }
  public string? SearchText { get; set; }

  private readonly Func<string, Task> _onSearchTextChanged;

  public SearchToolStripMenuItem(ToolStripDropDown parent, Func<string, Task> onSearchTextChanged)
  {
    parent.LayoutStyle = ToolStripLayoutStyle.VerticalStackWithOverflow;
    ParentDropDown = parent;
    ParentDropDown.Opacity = 0.95;
    ParentDropDown.TopLevel = true;
    ParentDropDown.AllowDrop = false;

    _onSearchTextChanged = onSearchTextChanged;

    AddSearchBox();
    AddMenuSeparator();
    RegisterEvents();
  }

  public ToolStripMenuItem AddMenuItem(
    string text,
    EventHandler? click = null,
    bool? visible = null,
    bool? isChecked = null,
    Image? image = null
  )
  {
    var item = new ToolStripMenuItem(text)
    {
      TextAlign = ContentAlignment.MiddleLeft,
      Checked = isChecked ?? false,
      Image = image,
      ImageScaling = ToolStripItemImageScaling.SizeToFit,
      ImageAlign = ContentAlignment.MiddleLeft
    };
    item.Click += click;
    if (visible == false)
    {
      item.Enabled = false;
    }

    ParentDropDown.Items.Add(item);
    return item;
  }

  public void AddMenuSeparator() => ParentDropDown.Items.Add(new ToolStripSeparator());

  private bool _suppressTextChanged;

  private void AddSearchBox()
  {
    var textBox = new TextBox
    {
      TextAlign = HorizontalAlignment.Left,
      BorderStyle = BorderStyle.None,
      Width = ParentDropDown.Width,
      Font = new Font("Segoe UI", 9),
      Text = SEARCH_PLACEHOLDER_TEXT,
      BackColor = Color.White,
    };

    textBox.Click += (_, __) =>
    {
      if (textBox.Text == SEARCH_PLACEHOLDER_TEXT)
      {
        _suppressTextChanged = true;
        textBox.Text = "";
        _suppressTextChanged = false;
      }
    };

    textBox.TextChanged += async (s, e) =>
    {
      if (_suppressTextChanged)
      {
        return;
      }
      SearchText = textBox.Text;
      var now = DateTime.UtcNow;
      if (now - _lastTime >= _debounce)
      {
        await _onSearchTextChanged.Invoke(textBox.Text);
      }
      _lastTime = now;
    };

    textBox.KeyDown += (s, e) =>
    {
      if (e.KeyCode == Keys.Escape)
      {
        textBox.Text = SEARCH_PLACEHOLDER_TEXT;
        _onSearchTextChanged.Invoke("");
        e.Handled = true;
      }
    };

    SearchHost = new ToolStripControlHost(textBox)
    {
      Alignment = ToolStripItemAlignment.Left,
      ControlAlign = ContentAlignment.MiddleLeft,
      Name = SearchItemId,
      Margin = new Padding(2),
      Padding = new Padding(2)
    };

    ParentDropDown.Items.Insert(0, SearchHost);
    textBox.Focus();
  }

  private void RegisterEvents()
  {
    ParentDropDown.ItemClicked += (sender, args) =>
    {
      // we are not closing the dropdown only if user clicked the first item of the dropdown which is TextBox that we use for search
      if (args.ClickedItem.Name == SearchItemId)
      {
        return;
      }

      ParentDropDown.Close();
    };

    // Resets the list with empty search texts, otherwise on next menu pop up we end up with latest state
    ParentDropDown.Closed += async (sender, args) =>
    {
      // clear list only if search text is not null
      if (SearchText != null)
      {
        await _onSearchTextChanged.Invoke("");
      }
      SearchText = null;
    };
  }
}
