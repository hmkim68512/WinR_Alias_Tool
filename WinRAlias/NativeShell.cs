using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WinRAlias;

/// <summary>셸 아이콘, Known Folder 경로, STA 스레드 등 Win32 헬퍼</summary>
internal static class NativeShell
{
    // ───────────────── 아이콘 ─────────────────

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(NativeSize size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int cx, cy; }

    private const int SIIGBF_ICONONLY = 0x04;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    /// <summary>
    /// 파일 경로 또는 셸 이름(shell:AppsFolder\AUMID 등)의 아이콘. 실패하면 null.
    /// 스토어 앱 아이콘도 얻을 수 있음. STA 스레드에서 호출할 것.
    /// </summary>
    public static Bitmap? GetIcon(string parsingName, int size)
    {
        if (string.IsNullOrWhiteSpace(parsingName)) return null;
        IShellItemImageFactory? factory = null;
        try
        {
            SHCreateItemFromParsingName(parsingName, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out factory);
            if (factory.GetImage(new NativeSize { cx = size, cy = size }, SIIGBF_ICONONLY, out var hbm) != 0)
                return null;
            try { return ToBitmap(hbm); }
            finally { DeleteObject(hbm); }
        }
        catch { return null; }
        finally
        {
            if (factory != null) Marshal.ReleaseComObject(factory);
        }
    }

    /// <summary>Image.FromHbitmap은 알파를 버리므로 픽셀을 직접 PARGB로 다시 읽음</summary>
    private static Bitmap ToBitmap(IntPtr hbm)
    {
        using var src = Image.FromHbitmap(hbm);
        if (Image.GetPixelFormatSize(src.PixelFormat) < 32) return new Bitmap(src);

        Bitmap? result = null;
        var data = src.LockBits(new Rectangle(0, 0, src.Width, src.Height), ImageLockMode.ReadOnly, src.PixelFormat);
        try
        {
            // 셸 비트맵은 bottom-up이라 Stride가 음수일 수 있음 → 줄 단위로 읽기
            bool hasAlpha = false;
            var row = new byte[data.Width * 4];
            for (int y = 0; y < data.Height && !hasAlpha; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                for (int i = 3; i < row.Length; i += 4)
                    if (row[i] != 0) { hasAlpha = true; break; }
            }

            if (hasAlpha)
            {
                using var view = new Bitmap(data.Width, data.Height, data.Stride, PixelFormat.Format32bppPArgb, data.Scan0);
                result = new Bitmap(view);
            }
        }
        finally { src.UnlockBits(data); }

        // 알파가 전부 0이면 불투명 비트맵으로 취급
        return result ?? new Bitmap(src);
    }

    // ───────────────── Known Folder ─────────────────

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, IntPtr token, out IntPtr path);

    /// <summary>{6D809377-...} 같은 KNOWNFOLDERID → 실제 폴더 경로</summary>
    public static string? KnownFolderPath(Guid id)
    {
        if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var p) != 0) return null;
        try { return Marshal.PtrToStringUni(p); }
        finally { Marshal.FreeCoTaskMem(p); }
    }

    // ───────────────── 일반 권한으로 실행 ─────────────────

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IComServiceProvider
    {
        void QueryService(ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        // IOleWindow 2개 + IShellBrowser 앞쪽 10개: 안 쓰지만 vtable 순서 때문에 자리만 잡음
        void _0(); void _1(); void _2(); void _3(); void _4(); void _5();
        void _6(); void _7(); void _8(); void _9(); void _10(); void _11();
        void QueryActiveShellView([MarshalAs(UnmanagedType.Interface)] out IShellView view);
    }

    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        // IOleWindow 2개 + IShellView 앞쪽 10개 (위와 같은 이유)
        void _0(); void _1(); void _2(); void _3(); void _4(); void _5();
        void _6(); void _7(); void _8(); void _9(); void _10(); void _11();
        void GetItemObject(uint item, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    }

    /// <summary>
    /// 바탕화면 셸(explorer)에게 ShellExecute를 대신 시킴 → explorer와 같은 권한(보통 일반 권한)으로 실행.
    /// 관리자로 실행 중일 때 여기서 띄운 프로그램이 관리자 권한을 물려받지 않게 하려는 것.
    /// Win+R과 같은 ShellExecute라서 App Paths alias도 그대로 해석된다.
    /// (Raymond Chen, "How can I launch an unelevated process from my elevated process, redux")
    /// UI(STA) 스레드에서 호출할 것.
    /// </summary>
    public static void ShellExecuteFromDesktop(string file, string? args = null)
    {
        const int SWC_DESKTOP = 8, SWFO_NEEDDISPATCH = 1, SW_SHOWNORMAL = 1, SVGIO_BACKGROUND = 0;
        var shellWindowsClsid = new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
        var sidTopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
        var iidShellBrowser = typeof(IShellBrowser).GUID;
        var iidDispatch = new Guid("00020400-0000-0000-C000-000000000046");

        dynamic shellWindows = Activator.CreateInstance(Type.GetTypeFromCLSID(shellWindowsClsid, throwOnError: true)!)!;
        object loc = 0;     // CSIDL_DESKTOP
        object root = null!; // VT_EMPTY
        object desktop = shellWindows.FindWindowSW(ref loc, ref root, SWC_DESKTOP, out int _, SWFO_NEEDDISPATCH)
                         ?? throw new InvalidOperationException("바탕화면 셸을 찾을 수 없어요.");

        ((IComServiceProvider)desktop).QueryService(ref sidTopLevelBrowser, ref iidShellBrowser, out var browser);
        ((IShellBrowser)browser).QueryActiveShellView(out var view);
        view.GetItemObject(SVGIO_BACKGROUND, ref iidDispatch, out var folderView);

        dynamic shellDispatch = ((dynamic)folderView).Application;
        shellDispatch.ShellExecute(file, args ?? "", "", "", SW_SHOWNORMAL);
    }

    // ───────────────── UI ─────────────────

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    public static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);

    // ───────────────── 스레드 ─────────────────

    /// <summary>Shell.Application 같은 아파트먼트 COM은 STA에서 돌려야 해서 전용 스레드로 실행</summary>
    public static Task<T> RunSta<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}
