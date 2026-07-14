using System.Drawing;
using System.Windows.Forms;

namespace PrivacyScreen.Windows;

internal sealed class PrivacyScreenAppContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _menu;
    private readonly HashSet<string> _targets;
    private readonly List<OverlayForm> _overlays = [];
    private readonly Timer _timer;
    private bool _isEnabled;
    private float _coverAlpha;
    private bool _frontalFocusEnabled;
    private float _frontalFocusWidth;

    public PrivacyScreenAppContext()
    {
        var settings = SettingsStore.Load();
        _coverAlpha = Math.Clamp(settings.CoverAlpha, 0.3f, 1.0f);
        _frontalFocusEnabled = settings.FrontalFocusEnabled;
        _frontalFocusWidth = Math.Clamp(settings.FrontalFocusWidth, 0.01f, 1.0f);
        _targets = new HashSet<string>(settings.TargetProcesses, StringComparer.OrdinalIgnoreCase);

        _menu = new ContextMenuStrip();
        _menu.Opening += (_, _) => RefreshMenu();

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Visible = true,
            Text = "PrivacyScreen",
            ContextMenuStrip = _menu
        };

        _timer = new Timer { Interval = 16 };
        _timer.Tick += (_, _) => UpdateOverlays();

        if (_targets.Count > 0)
        {
            SetEnabled(true);
        }
        else
        {
            RefreshMenu();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _menu.Dispose();
            ClearOverlays();
        }

        base.Dispose(disposing);
    }

    private void RefreshMenu()
    {
        _menu.Items.Clear();

        var targetLabel = _targets.Count == 0
            ? "Targets: none"
            : $"Targets: {string.Join(", ", _targets.OrderBy(v => v, StringComparer.OrdinalIgnoreCase))}";
        _menu.Items.Add(new ToolStripMenuItem(targetLabel) { Enabled = false });

        var toggleText = _isEnabled ? "Disable cover" : "Enable cover";
        var toggle = new ToolStripMenuItem(toggleText, null, (_, _) => ToggleEnabled())
        {
            Enabled = _targets.Count > 0
        };
        _menu.Items.Add(toggle);
        _menu.Items.Add(new ToolStripSeparator());

        var targetMenu = new ToolStripMenuItem("Select target apps (multiple)");
        var running = Win32.EnumerateSelectableProcesses();
        foreach (var processName in running)
        {
            var item = new ToolStripMenuItem(processName)
            {
                Checked = _targets.Contains(processName)
            };
            item.Click += (_, _) => ToggleTarget(processName);
            targetMenu.DropDownItems.Add(item);
        }

        if (running.Count == 0)
        {
            targetMenu.DropDownItems.Add(new ToolStripMenuItem("(no visible apps)") { Enabled = false });
        }

        _menu.Items.Add(targetMenu);
        _menu.Items.Add(CreateDarknessControl());
        _menu.Items.Add(CreateFrontalFocusToggle());
        _menu.Items.Add(CreateFrontalFocusWidthControl());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Quit", null, (_, _) => ExitThread()));
    }

    private ToolStripControlHost CreateDarknessControl()
    {
        var panel = new Panel
        {
            Width = 220,
            Height = 52
        };

        var label = new Label
        {
            Text = "Darkness",
            Left = 8,
            Top = 4,
            Width = 120
        };
        panel.Controls.Add(label);

        var trackBar = new TrackBar
        {
            Left = 8,
            Top = 20,
            Width = 200,
            TickStyle = TickStyle.None,
            Minimum = 30,
            Maximum = 100,
            Value = (int)Math.Round(_coverAlpha * 100f)
        };
        trackBar.ValueChanged += (_, _) =>
        {
            _coverAlpha = trackBar.Value / 100f;
            foreach (var overlay in _overlays)
            {
                overlay.SetAppearance(_coverAlpha, _frontalFocusEnabled, _frontalFocusWidth);
            }

            SaveSettings();
        };
        panel.Controls.Add(trackBar);

        return new ToolStripControlHost(panel)
        {
            AutoSize = false,
            Width = panel.Width + 4,
            Height = panel.Height + 4
        };
    }

    private ToolStripMenuItem CreateFrontalFocusToggle()
    {
        var item = new ToolStripMenuItem("Vertical privacy lines (software)")
        {
            Checked = _frontalFocusEnabled
        };
        item.Click += (_, _) =>
        {
            _frontalFocusEnabled = !_frontalFocusEnabled;
            foreach (var overlay in _overlays)
            {
                overlay.SetAppearance(_coverAlpha, _frontalFocusEnabled, _frontalFocusWidth);
            }

            SaveSettings();
            RefreshMenu();
        };
        return item;
    }

    private ToolStripControlHost CreateFrontalFocusWidthControl()
    {
        var panel = new Panel
        {
            Width = 220,
            Height = 52
        };

        var label = new Label
        {
            Text = "Line spacing",
            Left = 8,
            Top = 4,
            Width = 160
        };
        panel.Controls.Add(label);

        var trackBar = new TrackBar
        {
            Left = 8,
            Top = 20,
            Width = 200,
            TickStyle = TickStyle.None,
            Minimum = 1,
            Maximum = 100,
            Value = (int)Math.Round(_frontalFocusWidth * 100f),
            Enabled = _frontalFocusEnabled
        };
        trackBar.ValueChanged += (_, _) =>
        {
            _frontalFocusWidth = trackBar.Value / 100f;
            foreach (var overlay in _overlays)
            {
                overlay.SetAppearance(_coverAlpha, _frontalFocusEnabled, _frontalFocusWidth);
            }

            SaveSettings();
        };
        panel.Controls.Add(trackBar);

        return new ToolStripControlHost(panel)
        {
            AutoSize = false,
            Width = panel.Width + 4,
            Height = panel.Height + 4
        };
    }

    private void ToggleTarget(string processName)
    {
        if (_targets.Contains(processName))
        {
            _targets.Remove(processName);
        }
        else
        {
            _targets.Add(processName);
        }

        if (_targets.Count == 0)
        {
            SetEnabled(false);
        }
        else if (_isEnabled)
        {
            UpdateOverlays();
        }

        SaveSettings();
        RefreshMenu();
    }

    private void ToggleEnabled()
    {
        SetEnabled(!_isEnabled);
        RefreshMenu();
    }

    private void SetEnabled(bool enabled)
    {
        if (enabled && _targets.Count == 0)
        {
            _isEnabled = false;
            return;
        }

        if (_isEnabled == enabled)
        {
            return;
        }

        _isEnabled = enabled;
        if (enabled)
        {
            EnsureOverlayForms();
            _timer.Start();
            UpdateOverlays();
        }
        else
        {
            _timer.Stop();
            ClearOverlays();
        }
    }

    private void EnsureOverlayForms()
    {
        var screens = Screen.AllScreens;
        var requiresRebuild = _overlays.Count != screens.Length;
        if (!requiresRebuild)
        {
            for (var i = 0; i < screens.Length; i++)
            {
                if (_overlays[i].Bounds != screens[i].Bounds)
                {
                    requiresRebuild = true;
                    break;
                }
            }
        }

        if (!requiresRebuild)
        {
            return;
        }

        ClearOverlays();
        foreach (var screen in screens)
        {
            var overlay = new OverlayForm(screen.Bounds, _coverAlpha, _frontalFocusEnabled, _frontalFocusWidth);
            overlay.Show();
            _overlays.Add(overlay);
        }
    }

    private void ClearOverlays()
    {
        foreach (var overlay in _overlays)
        {
            overlay.Close();
            overlay.Dispose();
        }

        _overlays.Clear();
    }

    private void UpdateOverlays()
    {
        if (!_isEnabled)
        {
            return;
        }

        EnsureOverlayForms();
        var (fillRects, clipRects) = ComputeCoverGeometry();

        for (var i = 0; i < _overlays.Count; i++)
        {
            var screen = Screen.AllScreens[i];
            var fillLocal = IntersectAndLocalize(fillRects, screen.Bounds);
            var clipLocal = IntersectAndLocalize(clipRects, screen.Bounds);
            _overlays[i].SetGeometry(clipLocal, fillLocal);
        }
    }

    private (List<Rectangle> fillRects, List<Rectangle> clipRects) ComputeCoverGeometry()
    {
        var windows = Win32.EnumerateTopLevelWindows(_targets);
        var fillRects = new List<Rectangle>();
        var clipRects = new List<Rectangle>();

        for (var i = 0; i < windows.Count; i++)
        {
            var win = windows[i];
            if (!win.IsTarget)
            {
                continue;
            }

            fillRects.Add(win.Bounds);
            var visiblePieces = new List<Rectangle> { win.Bounds };
            for (var frontIndex = 0; frontIndex < i; frontIndex++)
            {
                if (windows[frontIndex].IsTarget)
                {
                    continue;
                }

                visiblePieces = visiblePieces
                    .SelectMany(rect => Subtract(rect, windows[frontIndex].Bounds))
                    .ToList();

                if (visiblePieces.Count == 0)
                {
                    break;
                }
            }

            clipRects.AddRange(visiblePieces);
        }

        return (fillRects, clipRects);
    }

    private static IEnumerable<Rectangle> IntersectAndLocalize(IEnumerable<Rectangle> rects, Rectangle screenBounds)
    {
        foreach (var rect in rects)
        {
            var inter = Rectangle.Intersect(rect, screenBounds);
            if (inter.Width <= 0 || inter.Height <= 0)
            {
                continue;
            }

            yield return new Rectangle(
                inter.Left - screenBounds.Left,
                inter.Top - screenBounds.Top,
                inter.Width,
                inter.Height);
        }
    }

    private static IEnumerable<Rectangle> Subtract(Rectangle rect, Rectangle hole)
    {
        var inter = Rectangle.Intersect(rect, hole);
        if (inter.Width <= 0 || inter.Height <= 0)
        {
            yield return rect;
            yield break;
        }

        if (inter.Bottom < rect.Bottom)
        {
            yield return Rectangle.FromLTRB(rect.Left, inter.Bottom, rect.Right, rect.Bottom);
        }

        if (inter.Top > rect.Top)
        {
            yield return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, inter.Top);
        }

        if (inter.Left > rect.Left)
        {
            yield return Rectangle.FromLTRB(rect.Left, inter.Top, inter.Left, inter.Bottom);
        }

        if (inter.Right < rect.Right)
        {
            yield return Rectangle.FromLTRB(inter.Right, inter.Top, rect.Right, inter.Bottom);
        }
    }

    private void SaveSettings()
    {
        SettingsStore.Save(new AppSettings
        {
            CoverAlpha = _coverAlpha,
            FrontalFocusEnabled = _frontalFocusEnabled,
            FrontalFocusWidth = _frontalFocusWidth,
            TargetProcesses = _targets.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList()
        });
    }
}
