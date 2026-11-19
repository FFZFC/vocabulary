using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using Vocabulary.Core;
using WinRT.Interop;

namespace Vocabulary;

public partial class App : Application
{
    internal static string IconPath => Path.Combine(AppContext.BaseDirectory, "Assets", "Vocabulary.ico");
    internal static string PortableDataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vocabulary");
    private static readonly Lazy<string?> PackageFamily = new(GetPackageFamily);
    internal static bool IsPackaged => PackageFamily.Value is not null;
    internal static string DataRoot => Environment.GetEnvironmentVariable("VOCABULARY_TEST_DATA")
        ?? (IsPackaged ? Windows.Storage.ApplicationData.Current.LocalFolder.Path : PortableDataRoot);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetCurrentPackageFamilyName(ref uint length, System.Text.StringBuilder? name);

    private static string? GetPackageFamily()
    {
        const int noPackage = 15700;
        const int insufficientBuffer = 122;
        uint length = 0;
        int result = GetCurrentPackageFamilyName(ref length, null);
        if (result == noPackage) return null;
        if (result != insufficientBuffer) throw new System.ComponentModel.Win32Exception(result);
        var name = new System.Text.StringBuilder((int)length);
        result = GetCurrentPackageFamilyName(ref length, name);
        if (result != 0) throw new System.ComponentModel.Win32Exception(result);
        return name.ToString();
    }
    internal StudyStore Store { get; private set; } = null!;
    internal StudySession Session { get; private set; } = null!;
    internal Dictionary<ChallengeKind, ChallengeRound> Challenges { get; } = [];
    private IReadOnlySet<string>? guessWords;
    internal IReadOnlySet<string> GuessWords => guessWords ??= File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Data", "wordle-words.txt")).ToHashSet(StringComparer.Ordinal);
    internal MainWindow Main { get; private set; } = null!;
    internal bool Ready { get; private set; }
    internal bool Exiting { get; private set; }
    internal bool DesktopAttached { get; private set; } = true;
    private WidgetWindow? widget;
    private Tray? tray;
    private Mutex? single;
    private EventWaitHandle? activation;
    private RegisteredWaitHandle? wait;
    private DispatcherQueueTimer? timer;
    private DateTime lastDate;
    private DateTimeOffset? wake;
    public App()
    {
        UnhandledException += (_, e) =>
        {
            try
            {
                Directory.CreateDirectory(DataRoot);
                File.WriteAllText(Path.Combine(DataRoot, "last-error.txt"), e.Exception.ToString());
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };
        InitializeComponent();
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            string suffix = Environment.GetEnvironmentVariable("VOCABULARY_TEST_DATA") is { } testPath
                ? "." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(testPath)))[..16]
                : PackageFamily.Value is { } family ? "." + family : "";
            activation = new(false, EventResetMode.AutoReset, @"Local\Vocabulary.Activate.1" + suffix);
            single = new(false, @"Local\Vocabulary.Instance.1" + suffix, out bool first);
            if (!first) { activation.Set(); Exit(); return; }
            Store = new(DataRoot, Path.Combine(AppContext.BaseDirectory, "Data", "lexicon.db"));
            Session = new(Store, () => DateTimeOffset.Now);
            Store.BookmarkChanged += Session.BookmarkChanged;
            Main = new(this);
            tray = new(Main, this);
            wait = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) => Main.DispatcherQueue.TryEnqueue(ShowMain), null, Timeout.Infinite, false);
            Session.Changed += () => { wake = Session.Current is null ? Store.Summary(Store.Load(), DateTimeOffset.Now).NextDue : null; };
            Main.Activate();
        }
        catch (Exception e)
        {
            Native.MessageBox(0, e.Message, "LexiPane · 无法启动", 0x10);
            Exit();
        }
    }
    internal void Begin()
    {
        if (Ready) return;
        Ready = true;
        lastDate = DateTime.Today;
        Session.StartRound();
        RecoverDesktop();
        timer = Main.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(4);
        timer.Tick += (_, _) =>
        {
            if (DateTime.Today != lastDate)
            {
                lastDate = DateTime.Today;
                Session.NewDay();
            }
            else if (wake is { } due && DateTimeOffset.Now >= due)
                Session.RefreshIfIdle();
            RecoverDesktop();
        };
        timer.Start();
    }
    internal int GlassDensity => int.TryParse(Store.Setting("glassDensity"), out var value) ? Math.Clamp(value, 0, 100) : 50;
    internal void SetDensity(int value)
    {
        Store.Set("glassDensity", value.ToString());
        widget?.SetDensity(value);
    }
    internal void UpdateLearningPlan(Preferences plan)
    {
        var before = Store.Load();
        Store.Save(plan);
        if (plan.Book != before.Book) Session.StartRound();
        widget?.UpdateGoal();
    }
    internal void StudyAction(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            Native.MessageBox(WindowNative.GetWindowHandle(Main), e.Message, "无法保存学习进度", 0x10);
        }
    }
    internal void ShowMain()
    {
        Main.AppWindow.Show();
        if (Main.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p && p.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized) p.Restore();
        Main.Activate();
        Native.SetForegroundWindow(WindowNative.GetWindowHandle(Main));
    }
    internal void RecoverDesktop()
    {
        if (!Ready || Exiting || !Store.Load().WidgetVisible) return;
        if (widget is null || !Native.IsWindow(WindowNative.GetWindowHandle(widget)))
        {
            var next = new WidgetWindow(this);
            widget = next;
            next.Closed += (_, _) => { if (widget == next) widget = null; };
        }
        DesktopAttached = widget.ShowOnDesktop();
        Main.SetDesktopStatus(DesktopAttached);
    }
    internal void DisplayChanged() { widget?.CorrectBounds(); RecoverDesktop(); }
    internal void ToggleWidget()
    {
        if (!Ready) return;
        var p = Store.Load();
        Store.Save(p with { WidgetVisible = !p.WidgetVisible });
        if (p.WidgetVisible) widget?.AppWindow.Hide(); else RecoverDesktop();
        Main.UpdateToggle();
    }
    internal void Quit(bool saveWidgetBounds = true)
    {
        if (Exiting) return;
        Exiting = true;
        timer?.Stop();
        if (saveWidgetBounds) widget?.SaveBounds();
        wait?.Unregister(null);
        tray?.Dispose();
        widget?.Close();
        Main.Close();
        Store.Dispose();
        activation?.Dispose(); single?.Dispose();
        Exit();
    }
}
