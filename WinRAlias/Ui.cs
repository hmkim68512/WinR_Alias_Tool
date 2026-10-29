namespace WinRAlias;

/// <summary>공통 폰트/색상/컨트롤. 크기는 96 DPI 기준 (폼이 AutoScaleMode.Dpi로 확대)</summary>
internal static class Ui
{
    public static readonly Font BaseFont = new("Segoe UI", 9.75f);
    public static readonly Font BoldFont = new("Segoe UI", 9.75f, FontStyle.Bold);
    public static readonly Font SmallFont = new("Segoe UI", 8.75f);
    public static readonly Font InputFont = new("Segoe UI", 11.25f);
    public static readonly Font TitleFont = new("Segoe UI", 13f, FontStyle.Bold);

    public static readonly Color PanelBack = Color.FromArgb(246, 247, 249);
    public static readonly Color Border = Color.FromArgb(222, 224, 228);
    public static readonly Color Muted = Color.FromArgb(100, 104, 112);
    public static readonly Color Ok = Color.FromArgb(16, 124, 65);
    public static readonly Color Warn = Color.FromArgb(178, 92, 0);
    public static readonly Color Error = Color.FromArgb(196, 43, 28);
    public static readonly Color Shadowed = Color.FromArgb(150, 150, 150);

    /// <summary>탐색기 스타일 + 깜빡임 없는 ListView</summary>
    public sealed class SmoothListView : ListView
    {
        public SmoothListView()
        {
            DoubleBuffered = true;
            View = View.Details;
            FullRowSelect = true;
            MultiSelect = false;
            HideSelection = false;
            BorderStyle = BorderStyle.FixedSingle;
            HeaderStyle = ColumnHeaderStyle.Nonclickable;
            Dock = DockStyle.Fill;
        }

        /// <summary>열 너비 비율. 창 크기가 바뀌어도 비율 유지</summary>
        public float[] ColumnWeights { get; set; } = Array.Empty<float>();

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeShell.SetWindowTheme(Handle, "Explorer", null);
            FitColumns();
        }

        protected override void OnClientSizeChanged(EventArgs e)
        {
            base.OnClientSizeChanged(e);
            FitColumns();
        }

        private void FitColumns()
        {
            int w = ClientSize.Width;
            if (w <= 0 || Columns.Count == 0 || ColumnWeights.Length != Columns.Count) return;
            float total = ColumnWeights.Sum();
            int used = 0;
            for (int i = 0; i < Columns.Count - 1; i++)
            {
                int cw = (int)(w * ColumnWeights[i] / total);
                Columns[i].Width = cw;
                used += cw;
            }
            Columns[^1].Width = Math.Max(40, w - used);
        }
    }

    public static SmoothListView MakeList(params (string Text, float Weight)[] cols)
    {
        var lv = new SmoothListView { ColumnWeights = cols.Select(c => c.Weight).ToArray() };
        foreach (var (t, _) in cols) lv.Columns.Add(t);
        return lv;
    }

    public static Button MakeButton(string text, EventHandler? onClick = null)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 3, 10, 3),
            UseVisualStyleBackColor = true
        };
        if (onClick != null) b.Click += onClick;
        return b;
    }

    /// <summary>TableLayoutPanel 안에서 너비에 맞춰 줄바꿈되는 라벨</summary>
    public static Label WrapLabel(Font? font = null, Color? color = null) => new()
    {
        AutoSize = true,
        Anchor = AnchorStyles.Left | AnchorStyles.Right,
        Font = font ?? BaseFont,
        ForeColor = color ?? SystemColors.ControlText,
        Margin = new Padding(0, 2, 0, 2)
    };

    public static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = SmallFont,
        ForeColor = Muted,
        Margin = new Padding(0, 12, 0, 3)
    };

    public static LinkLabel Link(string text, Action onClick)
    {
        var l = new LinkLabel
        {
            Text = text,
            AutoSize = true,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Margin = new Padding(0, 4, 0, 4)
        };
        l.LinkClicked += (_, _) => onClick();
        return l;
    }

    public static string KindText(AppKind kind) => kind switch
    {
        AppKind.Store => "스토어",
        AppKind.Other => "시스템",
        _ => "데스크톱"
    };
}
