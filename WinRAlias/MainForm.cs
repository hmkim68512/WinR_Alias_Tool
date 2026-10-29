using System.Diagnostics;

namespace WinRAlias;

public sealed class MainForm : Form
{
    // 상단 (두 탭 공용 검색)
    private readonly TextBox _search = new()
    {
        Dock = DockStyle.Fill, Font = Ui.InputFont, Margin = new Padding(0, 0, 8, 0),
        PlaceholderText = "프로그램 이름, alias, 경로로 검색    (Ctrl+F)"
    };
    private readonly Button _refresh = Ui.MakeButton("새로고침 (F5)");
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill, Padding = new Point(16, 5) };
    private readonly TabPage _appsPage = new("설치된 프로그램") { Padding = new Padding(10), UseVisualStyleBackColor = true };
    private readonly TabPage _aliasPage = new("등록된 alias") { Padding = new Padding(10), UseVisualStyleBackColor = true };

    // 프로그램 탭
    private readonly Ui.SmoothListView _apps = Ui.MakeList(("프로그램", 32), ("alias", 14), ("종류", 9), ("경로", 45));
    private readonly RadioButton _filterAll = new() { Text = "전체", Checked = true, AutoSize = true };
    private readonly RadioButton _filterNone = new() { Text = "alias 없음", AutoSize = true };
    private readonly RadioButton _filterHas = new() { Text = "alias 있음", AutoSize = true };

    // 선택한 프로그램 상세 + 빠른 등록
    private readonly Panel _detail = new() { Dock = DockStyle.Right, Width = 340, BackColor = Ui.PanelBack, Padding = new Padding(18, 16, 18, 16) };
    private readonly TableLayoutPanel _detailBody = new() { Dock = DockStyle.Fill, ColumnCount = 1, Visible = false };
    private readonly Label _detailEmpty = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Ui.Muted,
        Text = "왼쪽 목록에서 프로그램을 선택하세요.\n\n검색 후 Enter → alias 입력 → Enter\n이면 바로 등록돼요."
    };
    private readonly PictureBox _dIcon = new() { Size = new Size(48, 48), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 12, 0) };
    private readonly Label _dName = Ui.WrapLabel(Ui.TitleFont);
    private readonly Label _dMeta = Ui.WrapLabel(Ui.SmallFont, Ui.Muted);
    private readonly TextBox _dPath = new()
    {
        ReadOnly = true, Multiline = true, BorderStyle = BorderStyle.None, BackColor = Ui.PanelBack,
        ForeColor = Ui.Muted, Font = Ui.SmallFont, Dock = DockStyle.Fill, TabStop = false, Margin = new Padding(0, 10, 0, 0)
    };
    private readonly Label _dAliases = Ui.WrapLabel(Ui.BoldFont);
    private readonly TextBox _dAlias = new() { Dock = DockStyle.Fill, Font = Ui.InputFont, Margin = new Padding(0, 0, 6, 0) };
    private readonly Button _dAdd = Ui.MakeButton("등록");
    private readonly Label _dHint = Ui.WrapLabel(Ui.SmallFont);
    private InstalledApp? _detailApp;

    // alias 탭
    private readonly Ui.SmoothListView _aliases = Ui.MakeList(("alias", 16), ("대상", 54), ("범위", 9), ("상태", 21));
    private readonly CheckBox _showSystem = new() { Text = "시스템(HKLM) alias 표시", Checked = true, AutoSize = true, Margin = new Padding(16, 6, 3, 3) };
    private readonly CheckBox _onlyMine = new() { Text = "내가 만든 것만", AutoSize = true, Margin = new Padding(8, 6, 3, 3) };

    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _counts = new() { ForeColor = Ui.Muted };

    private readonly ImageList _icons = new() { ColorDepth = ColorDepth.Depth32Bit };
    private readonly IconLoader _listIcons;
    private readonly IconLoader _bigIcons;
    private readonly Dictionary<string, Bitmap> _bigIconCache = new(StringComparer.OrdinalIgnoreCase);

    private List<InstalledApp> _allApps = new();
    private List<AliasEntry> _allAliases = new();
    private Dictionary<string, string> _aliasNamesByTarget = new(StringComparer.OrdinalIgnoreCase);
    private bool _scanning;

    public MainForm()
    {
        SuspendLayout();
        Text = "WinR Alias" + (AppPathsRegistry.IsElevated() ? "  [관리자]" : "");
        Font = Ui.BaseFont;
        Size = new Size(1180, 740);
        MinimumSize = new Size(860, 520);
        StartPosition = FormStartPosition.CenterScreen;

        int iconPx = LogicalToDeviceUnits(24);
        _icons.ImageSize = new Size(iconPx, iconPx);
        _apps.SmallImageList = _icons;
        _aliases.SmallImageList = _icons;
        _listIcons = new IconLoader(iconPx, OnListIconsLoaded, workers: 3);
        _bigIcons = new IconLoader(LogicalToDeviceUnits(48), OnBigIconsLoaded, workers: 1);

        _tabs.TabPages.Add(_appsPage);
        _tabs.TabPages.Add(_aliasPage);
        BuildAppsPage();
        BuildAliasPage();

        var strip = new StatusStrip { SizingGrip = true };
        strip.Items.Add(_status);
        strip.Items.Add(_counts);

        Controls.Add(_tabs);
        Controls.Add(BuildHeader());
        Controls.Add(strip);

        WireEvents();

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(false);
        PerformLayout();

        Shown += async (_, _) =>
        {
            _search.Focus();
            await RescanAsync(useCache: true);
        };
    }

    // ───────────────── 레이아웃 ─────────────────

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, RowCount = 1,
            Padding = new Padding(12, 12, 12, 8)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _refresh.Dock = DockStyle.Fill;
        _refresh.Click += async (_, _) => await RescanAsync(useCache: false);
        header.Controls.Add(_search, 0, 0);
        header.Controls.Add(_refresh, 1, 0);

        if (!AppPathsRegistry.IsElevated())
        {
            var admin = Ui.MakeButton("관리자로 재실행", (_, _) => RestartElevated());
            admin.Dock = DockStyle.Fill;
            admin.Margin = new Padding(6, 0, 0, 0);
            header.Controls.Add(admin, 2, 0);
        }
        return header;
    }

    private void BuildAppsPage()
    {
        var filterRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 6), WrapContents = false };
        filterRow.Controls.Add(_filterAll);
        filterRow.Controls.Add(_filterNone);
        filterRow.Controls.Add(_filterHas);
        filterRow.Controls.Add(new Label
        {
            Text = "↑↓ 이동 · Enter alias 입력 · 우클릭 메뉴", AutoSize = true, ForeColor = Ui.Muted,
            Font = Ui.SmallFont, Margin = new Padding(20, 6, 0, 0)
        });

        var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 10, 0) };
        listHost.Controls.Add(_apps);
        listHost.Controls.Add(filterRow);

        BuildDetailPanel();
        _appsPage.Controls.Add(listHost);
        _appsPage.Controls.Add(_detail);

        var menu = new ContextMenuStrip();
        menu.Items.Add("alias 추가 (고급 설정)…", null, (_, _) => AddFromSelectedAppAdvanced());
        menu.Items.Add("프로그램 실행", null, (_, _) => { if (SelectedApp() is { } a) Launch(a.Target); });
        menu.Items.Add("파일 위치 열기", null, (_, _) => { if (SelectedApp() is { } a) OpenLocation(a.Target); });
        menu.Items.Add("경로 복사", null, (_, _) => { if (SelectedApp() is { } a) CopyText(a.Target); });
        _apps.ContextMenuStrip = menu;
    }

    private void BuildDetailPanel()
    {
        var b = _detailBody;
        b.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // 아이콘 + 이름 + 종류
        var head = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 2, Margin = new Padding(0) };
        head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        head.Controls.Add(_dIcon, 0, 0);
        head.SetRowSpan(_dIcon, 2);
        head.Controls.Add(_dName, 1, 0);
        head.Controls.Add(_dMeta, 1, 1);

        var quick = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0) };
        quick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        quick.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _dAdd.Dock = DockStyle.Fill;
        _dAdd.Margin = new Padding(0);
        quick.Controls.Add(_dAlias, 0, 0);
        quick.Controls.Add(_dAdd, 1, 0);

        var links = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0, 14, 0, 0) };
        links.Controls.Add(Ui.Link("고급 설정으로 추가…", AddFromSelectedAppAdvanced));
        links.Controls.Add(Ui.Link("프로그램 실행해 보기", () => { if (_detailApp != null) Launch(_detailApp.Target); }));
        links.Controls.Add(Ui.Link("파일 위치 열기", () => { if (_detailApp != null) OpenLocation(_detailApp.Target); }));

        var divider = new Label { AutoSize = false, Height = 1, Dock = DockStyle.Fill, BackColor = Ui.Border, Margin = new Padding(0, 12, 0, 2) };

        void Row(Control c, SizeType type = SizeType.AutoSize, float h = 0)
        {
            b.RowStyles.Add(new RowStyle(type, h));
            b.Controls.Add(c, 0, b.RowStyles.Count - 1);
        }
        Row(head);
        Row(_dPath, SizeType.Absolute, 58);
        Row(divider);
        Row(Ui.Caption("등록된 alias"));
        Row(_dAliases);
        Row(Ui.Caption("새 alias  (Win+R에 입력할 이름)"));
        Row(quick);
        Row(_dHint);
        Row(links);
        Row(new Panel { Dock = DockStyle.Fill }, SizeType.Percent, 100);

        _detail.Controls.Add(_detailBody);
        _detail.Controls.Add(_detailEmpty);
    }

    private void BuildAliasPage()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 6), WrapContents = true };
        bar.Controls.Add(Ui.MakeButton("새 alias", (_, _) => AddNew()));
        bar.Controls.Add(Ui.MakeButton("수정", (_, _) => EditSelected()));
        bar.Controls.Add(Ui.MakeButton("삭제", (_, _) => DeleteSelected()));
        bar.Controls.Add(Ui.MakeButton("실행 테스트", (_, _) => TestSelected()));
        bar.Controls.Add(Ui.MakeButton("백업 파일 열기", (_, _) => OpenBackup()));
        bar.Controls.Add(_showSystem);
        bar.Controls.Add(_onlyMine);

        var legend = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
        void Key(string text, Color color)
        {
            legend.Controls.Add(new Label { Text = "■ " + text, AutoSize = true, ForeColor = color, Font = Ui.SmallFont, Margin = new Padding(0, 0, 16, 0) });
        }
        Key("이 앱이 만든 alias", Ui.Ok);
        Key("다른 프로그램이 등록한 alias", SystemColors.ControlText);
        Key("대상 파일 없음", Ui.Error);
        Key("HKCU의 같은 이름에 가려짐", Ui.Shadowed);
        legend.Controls.Add(new Label
        {
            Text = "Enter 수정 · Delete 삭제 · 더블클릭 수정", AutoSize = true, ForeColor = Ui.Muted,
            Font = Ui.SmallFont, Margin = new Padding(12, 0, 0, 0)
        });

        _aliasPage.Controls.Add(_aliases);
        _aliasPage.Controls.Add(bar);
        _aliasPage.Controls.Add(legend);

        var menu = new ContextMenuStrip();
        menu.Items.Add("수정…", null, (_, _) => EditSelected());
        menu.Items.Add("삭제", null, (_, _) => DeleteSelected());
        menu.Items.Add("실행 테스트", null, (_, _) => TestSelected());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("파일 위치 열기", null, (_, _) => { if (SelectedAlias() is { } a) OpenLocation(a.ResolvedTarget); });
        menu.Items.Add("대상 경로 복사", null, (_, _) => { if (SelectedAlias() is { } a) CopyText(a.DisplayTarget); });
        _aliases.ContextMenuStrip = menu;
    }

    private void WireEvents()
    {
        _search.TextChanged += (_, _) => { RenderApps(); RenderAliases(); };
        _search.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape && _search.TextLength > 0) { _search.Clear(); e.SuppressKeyPress = true; }
            else if (e.KeyCode is Keys.Down or Keys.Enter)
            {
                e.SuppressKeyPress = true;
                var lv = CurrentList();
                if (lv.Items.Count == 0) return;
                if (lv.SelectedItems.Count == 0) lv.Items[0].Selected = true;
                lv.SelectedItems[0].Focused = true;
                // 프로그램 탭에서 Enter → 바로 alias 입력칸으로 (검색 → Enter → 이름 → Enter)
                if (e.KeyCode == Keys.Enter && lv == _apps) FocusQuickAlias();
                else lv.Focus();
            }
        };

        foreach (var rb in new[] { _filterAll, _filterNone, _filterHas })
            rb.CheckedChanged += (s, _) => { if (((RadioButton)s!).Checked) RenderApps(); };

        _apps.SelectedIndexChanged += (_, _) => UpdateDetail();
        _apps.DoubleClick += (_, _) => FocusQuickAlias();
        _apps.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FocusQuickAlias(); }
        };
        _apps.KeyPress += (_, e) => TypeIntoSearch(e);

        _dAlias.TextChanged += (_, _) => UpdateQuickHint();
        _dAlias.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; RegisterQuick(); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; _apps.Focus(); }
        };
        _dAdd.Click += (_, _) => RegisterQuick();

        _showSystem.CheckedChanged += (_, _) => RenderAliases();
        _onlyMine.CheckedChanged += (_, _) => RenderAliases();
        _aliases.DoubleClick += (_, _) => EditSelected();
        _aliases.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; EditSelected(); }
            else if (e.KeyCode == Keys.Delete) { e.SuppressKeyPress = true; DeleteSelected(); }
        };
        _aliases.KeyPress += (_, e) => TypeIntoSearch(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.F:
            case Keys.Control | Keys.L:
            case Keys.F3:
                _search.Focus();
                _search.SelectAll();
                return true;
            case Keys.F5:
                _ = RescanAsync(useCache: false);
                return true;
            case Keys.Control | Keys.N:
                AddNew();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>목록에 포커스가 있을 때 글자를 치면 검색창으로 (Spotlight처럼)</summary>
    private void TypeIntoSearch(KeyPressEventArgs e)
    {
        if (char.IsControl(e.KeyChar)) return;
        e.Handled = true;
        _search.Focus();
        _search.AppendText(e.KeyChar.ToString());
    }

    // ───────────────── 데이터 로딩 ─────────────────

    private async Task RescanAsync(bool useCache)
    {
        if (_scanning) return;
        _scanning = true;
        _refresh.Enabled = false;
        try
        {
            ReloadAliases(render: false);

            if (useCache && AppScanner.LoadCache() is { Count: > 0 } cached)
            {
                _allApps = cached;
                RenderAll();
                SetStatus("지난 목록을 표시 중 · 설치된 프로그램을 다시 확인하고 있어요…");
            }
            else SetStatus("설치된 프로그램을 찾는 중…");

            var sw = Stopwatch.StartNew();
            _allApps = await NativeShell.RunSta(AppScanner.Scan);
            AppScanner.SaveCache(_allApps);
            RenderAll();
            SetStatus($"프로그램 {_allApps.Count}개를 찾았어요 ({sw.Elapsed.TotalSeconds:0.0}초)");
        }
        catch (Exception ex)
        {
            SetStatus("스캔 실패: " + ex.Message);
        }
        finally
        {
            _scanning = false;
            _refresh.Enabled = true;
        }
    }

    private void ReloadAliases(bool render = true)
    {
        try { _allAliases = AppPathsRegistry.LoadAll(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "alias 읽기 실패", MessageBoxButtons.OK, MessageBoxIcon.Error); }

        // 대상 → alias 이름들 (프로그램 목록에 현재 alias 표시용). 가려진 항목은 실제로 안 쓰이니 제외.
        // 스토어 앱이 직접 등록한 alias(패키지 폴더 안 exe)는 패키지 이름으로도 찾을 수 있게
        _aliasNamesByTarget = _allAliases
            .Where(a => !a.ShadowedByUser)
            .SelectMany(a => new[] { (Key: a.EffectiveTarget, a.Name), (Key: PackageKey(a.PackageFamily), a.Name) })
            .Where(x => x.Key != null)
            .GroupBy(x => x.Key!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key,
                          g => string.Join(", ", g.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase)),
                          StringComparer.OrdinalIgnoreCase);
        if (render) RenderAll();
    }

    private void RenderAll()
    {
        RenderApps();
        RenderAliases();
        _counts.Text = $"프로그램 {_allApps.Count} · alias {_allAliases.Count} " +
                       $"(HKCU {_allAliases.Count(a => a.Scope == AliasScope.CurrentUser)} / " +
                       $"HKLM {_allAliases.Count(a => a.Scope == AliasScope.LocalMachine)})";
        // 화면에 보이는 건 Render*에서 먼저 요청했고, 나머지는 백그라운드로 천천히
        _listIcons.Request(_allApps.Select(a => a.IconKey).Concat(_allAliases.Select(AliasIconKey)));
    }

    private static string? PackageKey(string? packageFamily) =>
        packageFamily == null ? null : "pkg:" + packageFamily;

    private string AliasNamesFor(InstalledApp app)
    {
        var byTarget = _aliasNamesByTarget.GetValueOrDefault(app.Target, "");
        var byPackage = PackageKey(app.PackageFamily) is string pk ? _aliasNamesByTarget.GetValueOrDefault(pk, "") : "";
        if (byPackage.Length == 0) return byTarget;
        if (byTarget.Length == 0) return byPackage;
        return string.Join(", ", (byTarget + ", " + byPackage).Split(", ").Distinct(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>alias가 이 프로그램을 실행하는지 (같은 대상이거나 같은 스토어 패키지)</summary>
    private static bool PointsTo(AliasEntry a, InstalledApp app) =>
        string.Equals(a.EffectiveTarget, app.Target, StringComparison.OrdinalIgnoreCase) ||
        (app.PackageFamily != null && string.Equals(a.PackageFamily, app.PackageFamily, StringComparison.OrdinalIgnoreCase));

    private static string AliasIconKey(AliasEntry a) => a.ShellTarget ?? a.ResolvedTarget;

    /// <summary>목록의 경로 열: shell:AppsFolder\ 접두어는 읽기 어려워서 앱 ID만</summary>
    private static string DisplayPath(string target) =>
        ShellLauncher.IsShellTarget(target) ? "앱 ID: " + target[ShellLauncher.Prefix.Length..] : target;

    /// <summary>검색어의 모든 단어가 어딘가에 있으면 통과. 점수가 낮을수록 위에 표시</summary>
    private static int? MatchScore(string query, string name, string alias, string path)
    {
        if (query.Length == 0) return 0;
        int score = 0;
        foreach (var term in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (name.StartsWith(term, StringComparison.CurrentCultureIgnoreCase)) score += 0;
            else if (alias.Split(", ").Any(a => a.StartsWith(term, StringComparison.OrdinalIgnoreCase))) score += 1;
            else if (name.Contains(" " + term, StringComparison.CurrentCultureIgnoreCase)) score += 2;
            else if (name.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                     alias.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 3;
            else if (path.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 6;
            else return null;
        }
        return score;
    }

    private void RenderApps()
    {
        var q = _search.Text.Trim();
        var keep = SelectedApp()?.Target;

        var rows = new List<(InstalledApp App, string Aliases, int Score)>();
        foreach (var app in _allApps)
        {
            var names = AliasNamesFor(app);
            if (_filterNone.Checked && names.Length > 0) continue;
            if (_filterHas.Checked && names.Length == 0) continue;
            if (MatchScore(q, app.DisplayName, names, app.Target) is not int score) continue;
            rows.Add((app, names, score));
        }
        if (q.Length > 0) rows = rows.OrderBy(r => r.Score).ToList(); // 안정 정렬이라 같은 점수는 이름순 유지

        _apps.BeginUpdate();
        _apps.Items.Clear();
        ListViewItem? reselect = null;
        foreach (var (app, names, _) in rows)
        {
            var item = new ListViewItem(new[] { app.DisplayName, names, Ui.KindText(app.Kind), DisplayPath(app.Target) })
            {
                Tag = app, ImageKey = app.IconKey, UseItemStyleForSubItems = false
            };
            item.SubItems[1].ForeColor = Ui.Ok;
            item.SubItems[1].Font = Ui.BoldFont;
            item.SubItems[2].ForeColor = Ui.Muted;
            item.SubItems[3].ForeColor = Ui.Muted;
            _apps.Items.Add(item);
            if (keep != null && string.Equals(app.Target, keep, StringComparison.OrdinalIgnoreCase)) reselect = item;
        }
        _apps.EndUpdate();

        // 검색 중이면 맨 위 결과를 자동 선택 → 바로 Enter로 등록 가능
        if (reselect == null && q.Length > 0 && _apps.Items.Count > 0) reselect = _apps.Items[0];
        if (reselect != null)
        {
            reselect.Selected = true;
            reselect.Focused = true;
            reselect.EnsureVisible();
        }
        RequestVisibleIcons(_apps);
        UpdateDetail();
    }

    private void RenderAliases()
    {
        var q = _search.Text.Trim();
        var keep = SelectedAlias();

        _aliases.BeginUpdate();
        _aliases.Items.Clear();
        foreach (var a in _allAliases)
        {
            if (!_showSystem.Checked && a.Scope == AliasScope.LocalMachine) continue;
            if (_onlyMine.Checked && !a.CreatedByThisApp) continue;
            if (MatchScore(q, a.Name, "", a.DisplayTarget) == null) continue;

            string state;
            if (!a.TargetExists) state = "대상 없음";
            else if (a.ShadowedByUser) state = "HKCU에 가려짐";
            else state = a.CreatedByThisApp ? "정상 · 이 앱이 만듦" : "정상";

            var item = new ListViewItem(new[]
            {
                a.Name, DisplayPath(a.DisplayTarget),
                a.Scope == AliasScope.CurrentUser ? "HKCU" : "HKLM",
                state
            }) { Tag = a, ImageKey = AliasIconKey(a) };

            if (!a.TargetExists) item.ForeColor = Ui.Error;
            else if (a.ShadowedByUser) item.ForeColor = Ui.Shadowed;
            else if (a.CreatedByThisApp) item.ForeColor = Ui.Ok;

            _aliases.Items.Add(item);
            if (keep != null && a.KeyName == keep.KeyName && a.Scope == keep.Scope) item.Selected = true;
        }
        _aliases.EndUpdate();
        RequestVisibleIcons(_aliases);
    }

    // ───────────────── 아이콘 ─────────────────

    /// <summary>지금 화면에 보이는 줄(+여유분)의 아이콘을 먼저 읽도록</summary>
    private void RequestVisibleIcons(ListView lv)
    {
        if (lv.Items.Count == 0) return;
        int first = lv.IsHandleCreated ? lv.TopItem?.Index ?? 0 : 0;
        var keys = lv.Items.Cast<ListViewItem>().Skip(first).Take(50).Select(i => i.ImageKey);
        _listIcons.Request(keys, urgent: true);
    }

    private void OnListIconsLoaded(IReadOnlyList<(string Key, Bitmap Icon)> batch)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, icon) in batch)
            if (!_icons.Images.ContainsKey(key)) { _icons.Images.Add(key, icon); keys.Add(key); }
        RefreshImageKeys(_apps, keys);
        RefreshImageKeys(_aliases, keys);
    }

    /// <summary>ImageList에 나중에 추가된 이미지는 ImageKey를 다시 넣어야 반영됨</summary>
    private static void RefreshImageKeys(ListView lv, HashSet<string> keys)
    {
        if (keys.Count == 0) return;
        lv.BeginUpdate();
        foreach (ListViewItem item in lv.Items)
        {
            var key = item.ImageKey;
            if (!keys.Contains(key)) continue;
            item.ImageKey = "";
            item.ImageKey = key;
        }
        lv.EndUpdate();
    }

    private void OnBigIconsLoaded(IReadOnlyList<(string Key, Bitmap Icon)> batch)
    {
        foreach (var (key, icon) in batch)
        {
            _bigIconCache[key] = icon;
            if (_detailApp != null && string.Equals(_detailApp.IconKey, key, StringComparison.OrdinalIgnoreCase))
                _dIcon.Image = icon;
        }
    }

    // ───────────────── 상세 패널 / 빠른 등록 ─────────────────

    private void UpdateDetail()
    {
        var app = SelectedApp();
        _detailBody.Visible = app != null;
        _detailEmpty.Visible = app == null;
        if (app == null) { _detailApp = null; return; }

        var names = AliasNamesFor(app);
        _dAliases.Text = names.Length > 0 ? names : "아직 없음";
        _dAliases.ForeColor = names.Length > 0 ? Ui.Ok : Ui.Muted;

        if (!ReferenceEquals(app, _detailApp))
        {
            _detailApp = app;
            _dName.Text = app.DisplayName;
            _dMeta.Text = $"{Ui.KindText(app.Kind)} 앱 · {app.Source}";
            _dPath.Text = app.Target;

            _dIcon.Image = _bigIconCache.GetValueOrDefault(app.IconKey);
            if (_dIcon.Image == null) _bigIcons.Request(new[] { app.IconKey }, urgent: true);

            _dAlias.Text = names.Length > 0 ? "" : SuggestAlias(app);
        }
        UpdateQuickHint();
    }

    private void FocusQuickAlias()
    {
        if (SelectedApp() == null) return;
        _dAlias.Focus();
        _dAlias.SelectAll();
    }

    private void UpdateQuickHint()
    {
        var app = _detailApp;
        if (app == null) return;
        var name = AppPathsRegistry.StripExe(_dAlias.Text.Trim());

        void Hint(string text, Color color, bool canAdd)
        {
            _dHint.Text = text;
            _dHint.ForeColor = color;
            _dAdd.Enabled = canAdd;
        }

        if (name.Length == 0) { Hint("이름을 입력하고 Enter를 누르면 등록돼요.", Ui.Muted, false); return; }
        if (AppPathsRegistry.ValidateName(name) is string err) { Hint(err, Ui.Error, false); return; }

        var key = AppPathsRegistry.ToKeyName(name);
        var existing = _allAliases
            .Where(a => string.Equals(a.KeyName, key, StringComparison.OrdinalIgnoreCase) && !a.ShadowedByUser)
            .OrderBy(a => a.Scope) // HKCU 먼저
            .FirstOrDefault();

        if (existing != null && PointsTo(existing, app))
        {
            Hint($"✔ 등록되어 있어요. Win+R → {existing.Name}", Ui.Ok, false);
            return;
        }
        if (existing != null)
        {
            var how = existing.Scope == AliasScope.CurrentUser ? "등록하면 덮어써요." : "등록하면 HKCU 쪽이 우선 적용돼요.";
            Hint($"⚠ '{existing.Name}'은(는) 이미 다른 대상에 쓰이고 있어요.\n→ {existing.DisplayTarget}\n{how}", Ui.Warn, true);
            return;
        }

        var inPath = AppPathsRegistry.FindInPath(name);
        if (inPath != null && !string.Equals(inPath, app.Target, StringComparison.OrdinalIgnoreCase))
        {
            Hint($"⚠ PATH에 같은 이름의 프로그램이 있어요.\n→ {inPath}\nWin+R에서는 이 alias가 먼저 실행돼요.", Ui.Warn, true);
            return;
        }

        Hint($"Win+R → {name} 으로 실행돼요.", Ui.Muted, true);
    }

    private void RegisterQuick()
    {
        if (_detailApp is not { } app || !_dAdd.Enabled) return;
        var name = AppPathsRegistry.StripExe(_dAlias.Text.Trim());
        if (!ConfirmOverwrite(AliasScope.CurrentUser, name, exceptKey: null)) return;
        SaveAlias(AliasScope.CurrentUser, name, app.Target, WorkingDirFor(app), oldKeyName: null);
    }

    /// <summary>
    /// 추천 alias: exe 이름 → 표시 이름 → 패키지 이름 순으로 영문/숫자만 남겨서.
    /// (Win+R에서 한글은 IME 전환이 필요해서 영문 우선)
    /// </summary>
    private static string SuggestAlias(InstalledApp app)
    {
        var candidates = new List<string>();
        if (!app.IsShellTarget) candidates.Add(Path.GetFileNameWithoutExtension(app.Target));
        candidates.Add(app.DisplayName);
        if (app.IsShellTarget)
        {
            var id = app.Target[ShellLauncher.Prefix.Length..];
            candidates.Add(id.Split('!')[0].Split('_')[0].Split('.').Last());
        }

        foreach (var c in candidates)
        {
            var s = new string(c.ToLowerInvariant()
                                .Where(ch => ch is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_' or '.')
                                .ToArray()).Trim('.', '-', '_');
            if (s.Length >= 2 && s is not ("app" or "launcher" or "main" or "start")) return s;
        }
        return "";
    }

    private static string? WorkingDirFor(InstalledApp app) =>
        app.Kind == AppKind.Desktop && app.Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(app.Target)
            : null;

    // ───────────────── alias 추가 / 수정 / 삭제 ─────────────────

    private void AddFromSelectedAppAdvanced()
    {
        if (SelectedApp() is not { } app) return;
        var name = _dAlias.Text.Trim();
        ShowEditor("alias 추가", name.Length > 0 ? name : SuggestAlias(app), app.Target, WorkingDirFor(app),
                   AliasScope.CurrentUser, scopeLocked: false, original: null);
    }

    private void AddNew() =>
        ShowEditor("새 alias", "", "", null, AliasScope.CurrentUser, scopeLocked: false, original: null);

    private void EditSelected()
    {
        if (SelectedAlias() is not AliasEntry a) return;

        if (a.Scope == AliasScope.LocalMachine && !AppPathsRegistry.IsElevated())
        {
            // HKLM은 일반 권한으로 못 고침 → HKCU에 같은 이름으로 덮어쓰기 or 관리자 재실행
            var r = MessageBox.Show(this,
                $"'{a.Name}'은(는) 시스템(HKLM) alias라서 직접 수정하려면 관리자 권한이 필요해요.\n\n" +
                "[예]  현재 사용자(HKCU)에 같은 이름으로 덮어쓰기 (원본은 그대로)\n" +
                "[아니요]  관리자로 다시 실행해서 원본 수정",
                "시스템 alias 수정", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            if (r == DialogResult.Yes)
                ShowEditor($"'{a.Name}' 덮어쓰기 (HKCU)", a.Name, a.DisplayTarget, a.WorkingPath,
                           AliasScope.CurrentUser, scopeLocked: true, original: null);
            else if (r == DialogResult.No)
                RestartElevated();
            return;
        }

        if (!a.CreatedByThisApp)
        {
            var r = MessageBox.Show(this,
                $"'{a.Name}'은(는) 다른 프로그램이 등록한 alias예요. 수정하면 그 프로그램 동작에 영향이 갈 수 있어요.\n" +
                "수정 전 값은 백업 파일에 저장돼요. 계속할까요?",
                "확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;
        }

        ShowEditor($"'{a.Name}' 수정", a.Name, a.DisplayTarget, a.WorkingPath,
                   a.Scope, scopeLocked: true, original: a);
    }

    private void ShowEditor(string title, string name, string target, string? path,
                            AliasScope scope, bool scopeLocked, AliasEntry? original)
    {
        using var dlg = new AliasDialog(title, name, target, path, scope, scopeLocked);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (!ConfirmOverwrite(dlg.Scope, dlg.AliasName, original?.KeyName)) return;
        SaveAlias(dlg.Scope, dlg.AliasName, dlg.Target, dlg.WorkingPath, original?.KeyName);
    }

    /// <summary>같은 범위에 같은 이름이 이미 있으면 덮어쓸지 확인 (수정 중인 자기 자신은 제외)</summary>
    private bool ConfirmOverwrite(AliasScope scope, string name, string? exceptKey)
    {
        var key = AppPathsRegistry.ToKeyName(name);
        if (exceptKey != null && string.Equals(exceptKey, key, StringComparison.OrdinalIgnoreCase)) return true;

        var clash = _allAliases.FirstOrDefault(x => x.Scope == scope &&
            string.Equals(x.KeyName, key, StringComparison.OrdinalIgnoreCase));
        if (clash == null) return true;

        return MessageBox.Show(this,
            $"'{clash.Name}'이(가) 이미 있어요.\n→ {clash.DisplayTarget}\n\n덮어쓸까요? (기존 값은 백업돼요)",
            "덮어쓰기", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
    }

    private void SaveAlias(AliasScope scope, string name, string target, string? workingPath, string? oldKeyName)
    {
        try
        {
            AppPathsRegistry.Save(scope, name, target, workingPath, oldKeyName);
            var shown = AppPathsRegistry.StripExe(name.Trim());
            ReloadAliases();
            SetStatus($"✔ '{shown}' 저장 완료 — Win+R에서 {shown} 입력하면 실행돼요.");
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show(this, "권한이 없어요. HKLM은 관리자로 실행해야 수정할 수 있어요.", "저장 실패",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DeleteSelected()
    {
        if (SelectedAlias() is not AliasEntry a) return;

        if (a.Scope == AliasScope.LocalMachine && !AppPathsRegistry.IsElevated())
        {
            MessageBox.Show(this, "시스템(HKLM) alias를 삭제하려면 관리자 권한이 필요해요.\n상단의 [관리자로 재실행]을 눌러 주세요.",
                            "권한 필요", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var warn = a.CreatedByThisApp ? "" :
            "\n\n⚠ 다른 프로그램이 등록한 alias예요. 지우면 그 프로그램이 Win+R이나 다른 앱에서 안 열릴 수 있어요.";
        var r = MessageBox.Show(this,
            $"'{a.Name}' ({(a.Scope == AliasScope.CurrentUser ? "HKCU" : "HKLM")})을(를) 삭제할까요?\n기존 값은 백업 파일에 저장돼요.{warn}",
            "삭제", MessageBoxButtons.YesNo, a.CreatedByThisApp ? MessageBoxIcon.Question : MessageBoxIcon.Warning);
        if (r != DialogResult.Yes) return;

        try
        {
            AppPathsRegistry.Delete(a);
            ReloadAliases();
            SetStatus($"'{a.Name}' 삭제 완료");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "삭제 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Win+R과 같은 ShellExecute 경로로 실행해봄</summary>
    private void TestSelected()
    {
        if (SelectedAlias() is not AliasEntry a) return;
        try
        {
            StartAsUser(a.Name);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"'{a.Name}' 실행 실패: {ex.Message}", "실행 테스트", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ───────────────── 기타 동작 ─────────────────

    /// <summary>
    /// 관리자 모드여도 여기서 띄우는 프로그램은 일반 권한으로 (바탕화면 셸에게 대신 실행시킴).
    /// 그게 안 되면 관리자 권한으로 실행할지 물어봄.
    /// </summary>
    private void StartAsUser(string file, string? args = null)
    {
        if (AppPathsRegistry.IsElevated())
        {
            try
            {
                NativeShell.ShellExecuteFromDesktop(file, args);
                return;
            }
            catch (Exception ex)
            {
                var r = MessageBox.Show(this,
                    $"일반 권한으로 실행하지 못했어요. ({ex.Message})\n관리자 권한으로 실행할까요?",
                    "실행", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;
            }
        }
        Process.Start(new ProcessStartInfo(file, args ?? "") { UseShellExecute = true });
    }

    private void Launch(string target)
    {
        try
        {
            if (ShellLauncher.IsShellTarget(target)) StartAsUser("explorer.exe", target);
            else StartAsUser(target);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "실행 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenLocation(string path)
    {
        if (!PathUtil.FileExists(path))
        {
            SetStatus("파일 위치를 열 수 없는 항목이에요. (스토어 앱 등)");
            return;
        }
        try { StartAsUser("explorer.exe", $"/select,\"{path}\""); }
        catch (Exception ex) { SetStatus("파일 위치 열기 실패: " + ex.Message); }
    }

    private void CopyText(string text)
    {
        try
        {
            Clipboard.SetText(text);
            SetStatus("복사했어요: " + text);
        }
        catch { /* 클립보드 사용 중 */ }
    }

    private void OpenBackup()
    {
        var f = AppPathsRegistry.BackupFile;
        if (!File.Exists(f))
        {
            MessageBox.Show(this, "아직 백업된 항목이 없어요.", "백업", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try { StartAsUser("notepad.exe", $"\"{f}\""); }
        catch (Exception ex) { SetStatus("백업 파일 열기 실패: " + ex.Message); }
    }

    private void RestartElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            Close();
        }
        catch
        {
            // UAC 취소 시 예외 → 무시
        }
    }

    // ───────────────── 헬퍼 ─────────────────

    private void SetStatus(string text) => _status.Text = text;

    private ListView CurrentList() => _tabs.SelectedTab == _aliasPage ? _aliases : _apps;

    private InstalledApp? SelectedApp() =>
        _apps.SelectedItems.Count > 0 ? _apps.SelectedItems[0].Tag as InstalledApp : null;

    private AliasEntry? SelectedAlias() =>
        _aliases.SelectedItems.Count > 0 ? _aliases.SelectedItems[0].Tag as AliasEntry : null;
}
