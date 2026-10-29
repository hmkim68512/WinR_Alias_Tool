namespace WinRAlias;

/// <summary>alias 추가/수정 대화상자</summary>
public sealed class AliasDialog : Form
{
    private readonly TextBox _name = new() { Width = 400, Font = Ui.InputFont };
    private readonly TextBox _target = new() { Width = 400 };
    private readonly TextBox _path = new() { Width = 400, PlaceholderText = "비워두면 지정하지 않아요" };
    private readonly ComboBox _scope = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 400 };
    private readonly Label _warn = new() { AutoSize = true, MaximumSize = new Size(400, 0), Margin = new Padding(3, 8, 3, 0) };

    public string AliasName => AppPathsRegistry.StripExe(_name.Text.Trim());
    public string Target => _target.Text.Trim();
    public string? WorkingPath => string.IsNullOrWhiteSpace(_path.Text) ? null : _path.Text.Trim();
    public AliasScope Scope => _scope.SelectedIndex == 1 ? AliasScope.LocalMachine : AliasScope.CurrentUser;

    public AliasDialog(string title, string name, string target, string? workingPath,
                       AliasScope scope, bool scopeLocked)
    {
        SuspendLayout();
        Text = title;
        Font = Ui.BaseFont;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _name.Text = name;
        _target.Text = target;
        _path.Text = workingPath ?? "";

        _scope.Items.Add("현재 사용자 (HKCU) — 권한 필요 없음");
        _scope.Items.Add("모든 사용자 (HKLM) — 관리자 권한 필요");
        _scope.SelectedIndex = scope == AliasScope.LocalMachine ? 1 : 0;
        _scope.Enabled = !scopeLocked;
        if (!AppPathsRegistry.IsElevated() && !scopeLocked)
        {
            _scope.SelectedIndex = 0;
            _scope.Enabled = false; // 일반 권한이면 HKLM 선택 불가
        }

        var browse = Ui.MakeButton("찾아보기…");
        browse.Click += (_, _) =>
        {
            // DereferenceLinks=false: .lnk를 고르면 바로가기 자체를 대상으로 (인자가 필요한 앱용)
            using var ofd = new OpenFileDialog
            {
                Filter = "프로그램 (*.exe;*.lnk)|*.exe;*.lnk|모든 파일|*.*",
                DereferenceLinks = false
            };
            if (ofd.ShowDialog(this) == DialogResult.OK)
            {
                _target.Text = ofd.FileName;
                if (string.IsNullOrWhiteSpace(_name.Text))
                    _name.Text = Path.GetFileNameWithoutExtension(ofd.FileName).ToLowerInvariant().Replace(' ', '-');
            }
        };

        var ok = Ui.MakeButton("저장");
        var cancel = Ui.MakeButton("취소");
        cancel.DialogResult = DialogResult.Cancel;
        ok.Click += (_, _) => { if (ValidateInput()) DialogResult = DialogResult.OK; };
        AcceptButton = ok;
        CancelButton = cancel;

        _name.TextChanged += (_, _) => UpdateWarning();
        _target.TextChanged += (_, _) => UpdateWarning();

        var grid = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(16, 16, 16, 4) };
        void Row(string label, Control c, string? help = null, Control? extra = null)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 12, 0) });
            grid.Controls.Add(c);
            grid.Controls.Add(extra ?? new Label { AutoSize = true });
            if (help == null) return;
            grid.Controls.Add(new Label { AutoSize = true });
            grid.Controls.Add(new Label
            {
                Text = help, AutoSize = true, ForeColor = Ui.Muted, Font = Ui.SmallFont,
                MaximumSize = new Size(400, 0), Margin = new Padding(3, 0, 3, 8)
            });
            grid.Controls.Add(new Label { AutoSize = true });
        }
        Row("alias", _name, "Win+R에 입력할 이름이에요. 공백은 쓸 수 없어요.");
        Row("대상", _target, "exe 경로, 바로가기(.lnk), 또는 shell:AppsFolder\\앱ID", browse);
        Row("작업 폴더", _path, "실행할 때 PATH 앞에 추가돼요. 보통은 비워둬도 돼요.");
        Row("저장 위치", _scope);
        grid.Controls.Add(new Label { AutoSize = true });
        grid.Controls.Add(_warn);
        grid.Controls.Add(new Label { AutoSize = true });

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 12) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        var outer = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        outer.Controls.Add(grid);
        outer.Controls.Add(buttons);
        Controls.Add(outer);

        UpdateWarning();

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(false);
        PerformLayout();
    }

    private void UpdateWarning()
    {
        var msgs = new List<string>();
        var color = Ui.Muted;

        var err = AppPathsRegistry.ValidateName(_name.Text);
        if (err != null && _name.Text.Length > 0) { msgs.Add("⚠ " + err); color = Ui.Error; }

        if (err == null)
        {
            var inPath = AppPathsRegistry.FindInPath(_name.Text);
            if (inPath != null && !string.Equals(inPath, PathUtil.Normalize(_target.Text), StringComparison.OrdinalIgnoreCase))
            {
                msgs.Add($"⚠ PATH에 같은 이름이 있어요: {inPath}\nWin+R에서는 이 alias가 먼저 실행돼요. (cmd에서는 PATH 쪽)");
                if (color == Ui.Muted) color = Ui.Warn;
            }
        }

        if (ShellLauncher.IsShellTarget(_target.Text.Trim()))
            msgs.Add("ℹ exe가 없는 앱이라 실행용 바로가기를 만들어 연결해요.");
        else if (_target.Text.Length > 0 && !PathUtil.FileExists(PathUtil.Normalize(_target.Text)))
        {
            msgs.Add("⚠ 실행 파일이 존재하지 않아요.");
            if (color == Ui.Muted) color = Ui.Warn;
        }

        if (msgs.Count == 0 && err == null)
            msgs.Add($"Win+R → {AliasName} 으로 실행돼요.");

        _warn.Text = string.Join("\n", msgs);
        _warn.ForeColor = color;
    }

    private bool ValidateInput()
    {
        var err = AppPathsRegistry.ValidateName(_name.Text);
        if (err != null)
        {
            MessageBox.Show(this, err, "alias 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (string.IsNullOrWhiteSpace(_target.Text))
        {
            MessageBox.Show(this, "대상(실행 파일 경로)을 입력해 주세요.", "대상 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (!ShellLauncher.IsShellTarget(Target) && !PathUtil.FileExists(PathUtil.Normalize(_target.Text)))
        {
            var r = MessageBox.Show(this, "파일이 존재하지 않아요. 그래도 저장할까요?", "확인",
                                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return false;
        }
        return true;
    }
}
