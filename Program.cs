using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class Program
{
    [STAThread] static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        bool test = args.Contains("--self-test");
        using var mutex = new Mutex(true, test ? "Local\\iTunesInvert.Utility.SelfTest" : "Local\\iTunesInvert.Utility", out bool first);
        if (!first) return;
        if (!Native.MagInitialize()) { MessageBox.Show("Windows could not initialize the color filter.", "iTunes Invert"); return; }
        try { Application.Run(new Controller(test)); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "iTunes Invert"); }
        finally { Native.MagUninitialize(); }
    }
}

sealed class Overlay : Form
{
    public IntPtr Magnifier;
    public Overlay()
    {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        Bounds = new Rectangle(0, 0, 1, 1);
        _ = Handle;
        if (!Native.SetLayeredWindowAttributes(Handle, 0, 255, 2)) throw new Exception("Cannot initialize the overlay.");
        Magnifier = Native.CreateWindowEx(0, "Magnifier", "", 0x50000000, 0, 0, 1, 1, Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (Magnifier == IntPtr.Zero) throw new Exception("Cannot create the Windows color filter.");
        var transform = new Native.Matrix { Values = new float[] {1,0,0,0,1,0,0,0,1} };
        var colors = new Native.ColorMatrix { Values = new float[] {-1,0,0,0,0, 0,-1,0,0,0, 0,0,-1,0,0, 0,0,0,1,0, 1,1,1,0,1} };
        if (!Native.MagSetWindowTransform(Magnifier, ref transform) || !Native.MagSetColorEffect(Magnifier, ref colors)
            || !Native.MagSetWindowFilterList(Magnifier, 0, 1, new[] {Handle})) throw new Exception("Cannot configure the Windows color filter.");
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x80000 | 0x20 | 0x08000000 | 0x80; return p; } }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x84) { m.Result = new IntPtr(-1); return; }
        if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; }
        base.WndProc(ref m);
    }
    public bool Track(Native.Rect r)
    {
        int w = r.Right-r.Left, h = r.Bottom-r.Top;
        Native.SetWindowPos(Handle, new IntPtr(-1), r.Left, r.Top, w, h, 0x10 | 0x40);
        Native.SetWindowPos(Magnifier, IntPtr.Zero, 0, 0, w, h, 0x14);
        bool ok = Native.MagSetWindowSource(Magnifier, r);
        Native.InvalidateRect(Magnifier, IntPtr.Zero, false);
        return ok;
    }
}

sealed class Controller : ApplicationContext
{
    readonly Overlay overlay = new();
    readonly NotifyIcon tray;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 33 };
    readonly bool test;
    Form? testWindow;
    bool paused;
    int frames;
    public Controller(bool test)
    {
        this.test = test;
        var menu = new ContextMenuStrip();
        var pause = new ToolStripMenuItem("Pause inversion") { CheckOnClick = true };
        pause.CheckedChanged += (_,_) => { paused = pause.Checked; if (paused) overlay.Hide(); };
        menu.Items.Add(pause);
        menu.Items.Add("Quit", null, (_,_) => ExitThread());
        tray = new NotifyIcon { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application, Text = "iTunes Invert — waiting for iTunes", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_,_) => pause.Checked = !pause.Checked;
        Native.RegisterHotKey(overlay.Handle, 1, 0x4000 | 2 | 4, (int)Keys.F12);
        Application.AddMessageFilter(new ExitHotkey(ExitThread));
        if (test)
        {
            testWindow = new Form { Text = "Inversion test", BackColor = Color.White, Size = new Size(500,300) };
            testWindow.Controls.Add(new Label { Text = "White should appear black. Clicks pass through.", AutoSize = true, Location = new Point(20,20) });
            testWindow.Show();
        }
        timer.Tick += (_,_) => Tick(); timer.Start();
    }
    void Tick()
    {
        IntPtr target = test ? testWindow!.Handle : Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(target, out uint pid);
        bool isTarget = false;
        try { using var p = Process.GetProcessById((int)pid); isTarget = test ? target == testWindow?.Handle : p.ProcessName.Equals("iTunes", StringComparison.OrdinalIgnoreCase); } catch { }
        if (paused || !isTarget || Native.IsIconic(target) || !Native.GetWindowRect(target, out var r) || r.Right <= r.Left || r.Bottom <= r.Top)
        { overlay.Hide(); return; }
        // Use the visible DWM frame to avoid painting the invisible resize border.
        if (Native.DwmGetWindowAttribute(target, 9, out var frame, Marshal.SizeOf<Native.Rect>()) == 0) r = frame;
        // A topmost overlay must not recolor other topmost windows above iTunes.
        var region = Native.CreateRectRgn(0,0,r.Right-r.Left,r.Bottom-r.Top);
        for (var above = Native.GetWindow(target, 3); above != IntPtr.Zero; above = Native.GetWindow(above,3))
        {
            if (above == overlay.Handle || !Native.IsWindowVisible(above) || !Native.GetWindowRect(above, out var a)) continue;
            var cut = Native.CreateRectRgn(a.Left-r.Left,a.Top-r.Top,a.Right-r.Left,a.Bottom-r.Top);
            Native.CombineRgn(region, region, cut, 4); Native.DeleteObject(cut);
        }
        if (Native.SetWindowRgn(overlay.Handle, region, false) == 0) Native.DeleteObject(region);
        if (!overlay.Track(r)) { overlay.Hide(); tray.Text = "iTunes Invert — filter failed; quit and restart"; return; }
        tray.Text = "iTunes Invert — active (Ctrl+Alt+F12 quits)";
        if (test && ++frames >= 60)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"self-test.txt"), "PASS: initialization, inversion matrix, exclusion filter, 60 source updates, and window tracking. Visual appearance and input require manual verification.");
            ExitThread();
        }
    }
    protected override void ExitThreadCore()
    {
        timer.Stop(); timer.Dispose(); Native.UnregisterHotKey(overlay.Handle,1);
        overlay.Close(); overlay.Dispose(); tray.Visible = false; tray.Dispose(); testWindow?.Dispose(); base.ExitThreadCore();
    }
    sealed class ExitHotkey(Action quit) : IMessageFilter
    { public bool PreFilterMessage(ref Message m) { if (m.Msg == 0x312 && m.WParam == new IntPtr(1)) { quit(); return true; } return false; } }
}

static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct Matrix { [MarshalAs(UnmanagedType.ByValArray, SizeConst=9)] public float[] Values; }
    [StructLayout(LayoutKind.Sequential)] public struct ColorMatrix { [MarshalAs(UnmanagedType.ByValArray, SizeConst=25)] public float[] Values; }
    [DllImport("Magnification.dll")] public static extern bool MagInitialize();
    [DllImport("Magnification.dll")] public static extern bool MagUninitialize();
    [DllImport("Magnification.dll")] public static extern bool MagSetWindowTransform(IntPtr h, ref Matrix m);
    [DllImport("Magnification.dll")] public static extern bool MagSetColorEffect(IntPtr h, ref ColorMatrix m);
    [DllImport("Magnification.dll")] public static extern bool MagSetWindowFilterList(IntPtr h, uint mode, int count, IntPtr[] windows);
    [DllImport("Magnification.dll")] public static extern bool MagSetWindowSource(IntPtr h, Rect r);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr CreateWindowEx(int ex, string cls, string text, int style, int x,int y,int w,int h,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr param);
    [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr h,uint key,byte alpha,uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h,uint cmd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int ht,uint flags);
    [DllImport("user32.dll")] public static extern bool InvalidateRect(IntPtr h,IntPtr r,bool erase);
    [DllImport("user32.dll")] public static extern int SetWindowRgn(IntPtr h,IntPtr r,bool redraw);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,int key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h,int id);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int l,int t,int r,int b);
    [DllImport("gdi32.dll")] public static extern int CombineRgn(IntPtr dest,IntPtr a,IntPtr b,int mode);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h,int attr,out Rect r,int size);
}
