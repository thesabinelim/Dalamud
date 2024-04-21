using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

using Dalamud.Configuration.Internal;
using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Gui;
using Dalamud.Interface.FontIdentifier;
using Dalamud.Interface.Internal;
using Dalamud.Interface.Internal.ManagedAsserts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.ManagedFontAtlas.Internals;
using Dalamud.Utility;

using ImGuiNET;

using ImGuiScene;

using Serilog;

using SharpDX.Direct3D11;

namespace Dalamud.Interface;

/// <summary>
/// This class represents the Dalamud UI that is drawn on top of the game.
/// It can be used to draw custom windows and overlays.
/// </summary>
public sealed class UiBuilder : IDisposable
{
    private readonly Stopwatch stopwatch;
    private readonly HitchDetector hitchDetector;
    private readonly string namespaceName;
    private readonly InterfaceManager interfaceManager = Service<InterfaceManager>.Get();
    private readonly Framework framework = Service<Framework>.Get();

    [ServiceManager.ServiceDependency]
    private readonly DalamudConfiguration configuration = Service<DalamudConfiguration>.Get();

    private readonly DisposeSafety.ScopedFinalizer scopedFinalizer = new();

    private bool hasErrorWindow = false;
    private bool lastFrameUiHideState = false;

    private IFontHandle? defaultFontHandle;
    private IFontHandle? iconFontHandle;
    private IFontHandle? monoFontHandle;
    private IFontHandle? iconFontFixedWidthHandle;

    /// <summary>
    /// Initializes a new instance of the <see cref="UiBuilder"/> class and registers it.
    /// You do not have to call this manually.
    /// </summary>
    /// <param name="namespaceName">The plugin namespace.</param>
    internal UiBuilder(string namespaceName)
    {
        try
        {
            this.stopwatch = new Stopwatch();
            this.hitchDetector = new HitchDetector($"UiBuilder({namespaceName})", this.configuration.UiBuilderHitch);
            this.namespaceName = namespaceName;

            this.interfaceManager.Draw += this.OnDraw;
            this.scopedFinalizer.Add(() => this.interfaceManager.Draw -= this.OnDraw);

            this.interfaceManager.ResizeBuffers += this.OnResizeBuffers;
            this.scopedFinalizer.Add(() => this.interfaceManager.ResizeBuffers -= this.OnResizeBuffers);

            this.FontAtlas =
                this.scopedFinalizer
                    .Add(
                        Service<FontAtlasFactory>
                            .Get()
                            .CreateFontAtlas(namespaceName, FontAtlasAutoRebuildMode.Async));
        }
        catch
        {
            this.scopedFinalizer.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The event that gets called when Dalamud is ready to draw your windows or overlays.
    /// When it is called, you can use static ImGui calls.
    /// </summary>
    public event Action? Draw;

    /// <summary>
    /// The event that is called when the game's DirectX device is requesting you to resize your buffers.
    /// </summary>
    public event Action? ResizeBuffers;

    /// <summary>
    /// Event that is fired when the plugin should open its configuration interface.
    /// </summary>
    public event Action? OpenConfigUi;

    /// <summary>
    /// Event that is fired when the plugin should open its main interface.
    /// </summary>
    public event Action? OpenMainUi;

    /// <summary>
    /// Gets or sets an action that is called when plugin UI or interface modifications are supposed to be shown.
    /// These may be fired consecutively.
    /// </summary>
    public event Action? ShowUi;

    /// <summary>
    /// Gets or sets an action that is called when plugin UI or interface modifications are supposed to be hidden.
    /// These may be fired consecutively.
    /// </summary>
    public event Action? HideUi;

    /// <summary>
    /// Gets the default Dalamud font size in points.
    /// </summary>
    public static float DefaultFontSizePt => Service<FontAtlasFactory>.Get().DefaultFontSpec.SizePt;

    /// <summary>
    /// Gets the default Dalamud font size in pixels.
    /// </summary>
    public static float DefaultFontSizePx => Service<FontAtlasFactory>.Get().DefaultFontSpec.SizePx;

    /// <summary>
    /// Gets the default Dalamud font - supporting all game languages and icons.<br />
    /// <strong>Accessing this static property outside of <see cref="Draw"/> is dangerous and not supported.</strong>
    /// </summary>
    public static ImFontPtr DefaultFont => InterfaceManager.DefaultFont;

    /// <summary>
    /// Gets the default Dalamud icon font based on FontAwesome 5 Free solid.<br />
    /// <strong>Accessing this static property outside of <see cref="Draw"/> is dangerous and not supported.</strong>
    /// </summary>
    public static ImFontPtr IconFont => InterfaceManager.IconFont;

    /// <summary>
    /// Gets the default Dalamud monospaced font based on Inconsolata Regular.<br />
    /// <strong>Accessing this static property outside of <see cref="Draw"/> is dangerous and not supported.</strong>
    /// </summary>
    public static ImFontPtr MonoFont => InterfaceManager.MonoFont;

    /// <summary>
    /// Gets the default font specifications.
    /// </summary>
    public IFontSpec DefaultFontSpec => Service<FontAtlasFactory>.Get().DefaultFontSpec;

    /// <summary>
    /// Gets the handle to the default Dalamud font - supporting all game languages and icons.
    /// </summary>
    /// <remarks>
    /// A font handle corresponding to this font can be obtained with:
    /// <code>
    /// fontAtlas.NewDelegateFontHandle(
    ///     e => e.OnPreBuild(
    ///         tk => tk.AddDalamudDefaultFont(UiBuilder.DefaultFontSizePx)));
    /// </code>
    /// </remarks>
    public IFontHandle DefaultFontHandle =>
        this.defaultFontHandle ??=
            this.scopedFinalizer.Add(
                new FontHandleWrapper(
                    this.InterfaceManagerWithScene?.DefaultFontHandle
                    ?? throw new InvalidOperationException("Scene is not yet ready.")));

    /// <summary>
    /// Gets the default Dalamud icon font based on FontAwesome 5 Free solid.
    /// </summary>
    /// <remarks>
    /// A font handle corresponding to this font can be obtained with:
    /// <code>
    /// fontAtlas.NewDelegateFontHandle(
    ///     e => e.OnPreBuild(
    ///         tk => tk.AddFontAwesomeIconFont(new() { SizePt = UiBuilder.DefaultFontSizePt })));
    /// // or use
    ///         tk => tk.AddFontAwesomeIconFont(new() { SizePx = UiBuilder.DefaultFontSizePx })));
    /// </code>
    /// </remarks>
    public IFontHandle IconFontHandle =>
        this.iconFontHandle ??=
            this.scopedFinalizer.Add(
                new FontHandleWrapper(
                    this.InterfaceManagerWithScene?.IconFontHandle
                    ?? throw new InvalidOperationException("Scene is not yet ready.")));

    /// <summary>
    /// Gets the default Dalamud icon font based on FontAwesome 5 free solid with a fixed width and vertically centered glyphs.
    /// </summary>
    public IFontHandle IconFontFixedWidthHandle =>
        this.iconFontFixedWidthHandle ??=
            this.scopedFinalizer.Add(
                new FontHandleWrapper(
                    this.InterfaceManagerWithScene?.IconFontFixedWidthHandle
                    ?? throw new InvalidOperationException("Scene is not yet ready.")));

    /// <summary>
    /// Gets the default Dalamud monospaced font based on Inconsolata Regular.
    /// </summary>
    /// <remarks>
    /// A font handle corresponding to this font can be obtained with:
    /// <code>
    /// fontAtlas.NewDelegateFontHandle(
    ///     e => e.OnPreBuild(
    ///         tk => tk.AddDalamudAssetFont(
    ///             DalamudAsset.InconsolataRegular,
    ///             new() { SizePt = UiBuilder.DefaultFontSizePt })));
    /// // or use
    ///             new() { SizePx = UiBuilder.DefaultFontSizePx })));
    /// </code>
    /// </remarks>
    public IFontHandle MonoFontHandle =>
        this.monoFontHandle ??=
            this.scopedFinalizer.Add(
                new FontHandleWrapper(
                    this.InterfaceManagerWithScene?.MonoFontHandle
                    ?? throw new InvalidOperationException("Scene is not yet ready.")));

    /// <summary>
    /// Gets the game's active Direct3D device.
    /// </summary>
    public Device Device => this.InterfaceManagerWithScene!.Device!;

    /// <summary>
    /// Gets the game's main window handle.
    /// </summary>
    public IntPtr WindowHandlePtr => this.InterfaceManagerWithScene!.WindowHandlePtr;

    /// <summary>
    /// Gets or sets a value indicating whether this plugin should hide its UI automatically when the game's UI is hidden.
    /// </summary>
    public bool DisableAutomaticUiHide { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether this plugin should hide its UI automatically when the user toggles the UI.
    /// </summary>
    public bool DisableUserUiHide { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether this plugin should hide its UI automatically during cutscenes.
    /// </summary>
    public bool DisableCutsceneUiHide { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether this plugin should hide its UI automatically while gpose is active.
    /// </summary>
    public bool DisableGposeUiHide { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether or not the game's cursor should be overridden with the ImGui cursor.
    /// </summary>
    public bool OverrideGameCursor
    {
        get => this.interfaceManager.OverrideGameCursor;
        set => this.interfaceManager.OverrideGameCursor = value;
    }

    /// <summary>
    /// Gets the count of Draw calls made since plugin creation.
    /// </summary>
    public ulong FrameCount { get; private set; } = 0;

    /// <summary>
    /// Gets a value indicating whether or not a cutscene is playing.
    /// </summary>
    public bool CutsceneActive
    {
        get
        {
            var condition = Service<Condition>.GetNullable();
            if (condition == null)
                return false;
            return condition[ConditionFlag.OccupiedInCutSceneEvent]
                   || condition[ConditionFlag.WatchingCutscene78];
        }
    }

    /// <summary>
    /// Gets a value indicating whether this plugin should modify the game's interface at this time.
    /// </summary>
    public bool ShouldModifyUi => this.interfaceManager.IsDispatchingEvents;

    /// <summary>
    /// Gets a value indicating whether UI functions can be used.
    /// </summary>
    public bool UiPrepared => Service<InterfaceManager.InterfaceManagerWithScene>.GetNullable() != null;

    /// <summary>
    /// Gets the plugin-private font atlas.
    /// </summary>
    public IFontAtlas FontAtlas { get; }

    /// <summary>
    /// Gets a value indicating whether or not to use "reduced motion". This usually means that you should use less
    /// intrusive animations, or disable them entirely.
    /// </summary>
    public bool ShouldUseReducedMotion => Service<DalamudConfiguration>.Get().ReduceMotions ?? false;

    /// <summary>
    /// Gets or sets a value indicating whether statistics about UI draw time should be collected.
    /// </summary>
#if DEBUG
    internal static bool DoStats { get; set; } = true;
#else
    internal static bool DoStats { get; set; } = false;
#endif

    /// <summary>
    /// Gets a value indicating whether this UiBuilder has a configuration UI registered.
    /// </summary>
    internal bool HasConfigUi => this.OpenConfigUi != null;

    /// <summary>
    /// Gets a value indicating whether this UiBuilder has a configuration UI registered.
    /// </summary>
    internal bool HasMainUi => this.OpenMainUi != null;

    /// <summary>
    /// Gets or sets the time this plugin took to draw on the last frame.
    /// </summary>
    internal long LastDrawTime { get; set; } = -1;

    /// <summary>
    /// Gets or sets the longest amount of time this plugin ever took to draw.
    /// </summary>
    internal long MaxDrawTime { get; set; } = -1;

    /// <summary>
    /// Gets or sets a history of the last draw times, used to calculate an average.
    /// </summary>
    internal List<long> DrawTimeHistory { get; set; } = new List<long>();

    private InterfaceManager? InterfaceManagerWithScene =>
        Service<InterfaceManager.InterfaceManagerWithScene>.GetNullable()?.Manager;

    private Task<InterfaceManager> InterfaceManagerWithSceneAsync =>
        Service<InterfaceManager.InterfaceManagerWithScene>.GetAsync().ContinueWith(task => task.Result.Manager);

    /// <summary>
    /// Loads an image from the specified file.
    /// </summary>
    /// <param name="filePath">The full filepath to the image.</param>
    /// <returns>A <see cref="TextureWrap"/> object wrapping the created image.  Use <see cref="TextureWrap.ImGuiHandle"/> inside ImGui.Image().</returns>
    public IDalamudTextureWrap LoadImage(string filePath)
        => this.InterfaceManagerWithScene?.LoadImage(filePath)
           ?? throw new InvalidOperationException("Load failed.");

    /// <summary>
    /// Loads an image from a byte stream, such as a png downloaded into memory.
    /// </summary>
    /// <param name="imageData">A byte array containing the raw image data.</param>
    /// <returns>A <see cref="TextureWrap"/> object wrapping the created image.  Use <see cref="TextureWrap.ImGuiHandle"/> inside ImGui.Image().</returns>
    public IDalamudTextureWrap LoadImage(byte[] imageData)
        => this.InterfaceManagerWithScene?.LoadImage(imageData)
           ?? throw new InvalidOperationException("Load failed.");

    /// <summary>
    /// Loads an image from raw unformatted pixel data, with no type or header information.  To load formatted data, use <see cref="LoadImage(byte[])"/>.
    /// </summary>
    /// <param name="imageData">A byte array containing the raw pixel data.</param>
    /// <param name="width">The width of the image contained in <paramref name="imageData"/>.</param>
    /// <param name="height">The height of the image contained in <paramref name="imageData"/>.</param>
    /// <param name="numChannels">The number of channels (bytes per pixel) of the image contained in <paramref name="imageData"/>.  This should usually be 4.</param>
    /// <returns>A <see cref="TextureWrap"/> object wrapping the created image.  Use <see cref="TextureWrap.ImGuiHandle"/> inside ImGui.Image().</returns>
    public IDalamudTextureWrap LoadImageRaw(byte[] imageData, int width, int height, int numChannels)
        => this.InterfaceManagerWithScene?.LoadImageRaw(imageData, width, height, numChannels)
           ?? throw new InvalidOperationException("Load failed.");

    /// <summary>
    /// Loads an ULD file that can load textures containing multiple icons in a single texture.
    /// </summary>
    /// <param name="uldPath">The path of the requested ULD file.</param>
    /// <returns>A wrapper around said ULD file.</returns>
    public UldWrapper LoadUld(string uldPath)
        => new(this, uldPath);

    /// <summary>
    /// Asynchronously loads an image from the specified file, when it's possible to do so.
    /// </summary>
    /// <param name="filePath">The full filepath to the image.</param>
    /// <returns>A <see cref="TextureWrap"/> object wrapping the created image.  Use <see cref="TextureWrap.ImGuiHandle"/> inside ImGui.Image().</returns>
    public Task<IDalamudTextureWrap> LoadImageAsync(string filePath) => Task.Run(
        async () =>
            (await this.InterfaceManagerWithSceneAsync).LoadImage(filePath)
            ?? throw new InvalidOperationException("Load failed."));

    /// <summary>
    /// Asynchronously loads an image from a byte stream, such as a png downloaded into memory, when it's possible to do so.
    /// </summary>
    /// <param name="imageData">A byte array containing the raw image data.</param>
    /// <returns>A <see cref="TextureWrap"/> object wrapping the created image.  Use <see cref="TextureWrap.ImGuiHandle"/> inside ImGui.Image().</returns>
    public Task<IDalamudTextureWrap> LoadImageAsync(byte[] imageData) => Task.Run(
        async () =>
            (await this.InterfaceManagerWithSceneAsync).LoadImage(imageData)
            ?? throw new InvalidOperationException("Load failed."));

    /// <summary>
    /// Asynchronously loads an image from raw unformatted pixel data, with no type or header information, when it's possible to do so.  To load formatted data, use <see cref="LoadImage(byte[])"/>.
    /// </summary>
    /// <param name="imageData">A byte array containing the raw pixel data.</param>
    /// <param name="width">The width of the image contained in <paramref name="imageData"/>.</param>
    /// <param name="height">The height of the image contained in <paramref name="imageData"/>.</param>
    /// <param name="numChannels">The number of channels (bytes per pixel) of the image contained in <paramref name="imageData"/>.  This should usually be 4.</param>
    /// <returns>A <see cref="TextureWrap"/> object wrapping the created image.  Use <see cref="TextureWrap.ImGuiHandle"/> inside ImGui.Image().</returns>
    public Task<IDalamudTextureWrap> LoadImageRawAsync(byte[] imageData, int width, int height, int numChannels) => Task.Run(
        async () =>
            (await this.InterfaceManagerWithSceneAsync).LoadImageRaw(imageData, width, height, numChannels)
            ?? throw new InvalidOperationException("Load failed."));

    /// <summary>
    /// Waits for UI to become available for use.
    /// </summary>
    /// <returns>A task that completes when the game's Present has been called at least once.</returns>
    public Task WaitForUi() => this.InterfaceManagerWithSceneAsync;

    /// <summary>
    /// Waits for UI to become available for use.
    /// </summary>
    /// <param name="func">Function to call.</param>
    /// <param name="runInFrameworkThread">Specifies whether to call the function from the framework thread.</param>
    /// <returns>A task that completes when the game's Present has been called at least once.</returns>
    /// <typeparam name="T">Return type.</typeparam>
    public Task<T> RunWhenUiPrepared<T>(Func<T> func, bool runInFrameworkThread = false)
    {
        if (runInFrameworkThread)
        {
            return this.InterfaceManagerWithSceneAsync
                       .ContinueWith(_ => this.framework.RunOnFrameworkThread(func))
                       .Unwrap();
        }
        else
        {
            return this.InterfaceManagerWithSceneAsync
                       .ContinueWith(_ => func());
        }
    }

    /// <summary>
    /// Waits for UI to become available for use.
    /// </summary>
    /// <param name="func">Function to call.</param>
    /// <param name="runInFrameworkThread">Specifies whether to call the function from the framework thread.</param>
    /// <returns>A task that completes when the game's Present has been called at least once.</returns>
    /// <typeparam name="T">Return type.</typeparam>
    public Task<T> RunWhenUiPrepared<T>(Func<Task<T>> func, bool runInFrameworkThread = false)
    {
        if (runInFrameworkThread)
        {
            return this.InterfaceManagerWithSceneAsync
                       .ContinueWith(_ => this.framework.RunOnFrameworkThread(func))
                       .Unwrap();
        }
        else
        {
            return this.InterfaceManagerWithSceneAsync
                       .ContinueWith(_ => func())
                       .Unwrap();
        }
    }

    /// <summary>
    /// Creates an isolated <see cref="IFontAtlas"/>.
    /// </summary>
    /// <param name="autoRebuildMode">Specify when and how to rebuild this atlas.</param>
    /// <param name="isGlobalScaled">Whether the fonts in the atlas is global scaled.</param>
    /// <param name="debugName">Name for debugging purposes.</param>
    /// <returns>A new instance of <see cref="IFontAtlas"/>.</returns>
    /// <remarks>
    /// Use this to create extra font atlases, if you want to create and dispose fonts without having to rebuild all
    /// other fonts together.<br />
    /// If <paramref name="autoRebuildMode"/> is not <see cref="FontAtlasAutoRebuildMode.OnNewFrame"/>,
    /// the font rebuilding functions must be called manually.
    /// </remarks>
    public IFontAtlas CreateFontAtlas(
        FontAtlasAutoRebuildMode autoRebuildMode,
        bool isGlobalScaled = true,
        string? debugName = null) =>
        this.scopedFinalizer.Add(Service<FontAtlasFactory>
                                 .Get()
                                 .CreateFontAtlas(
                                     this.namespaceName + ":" + (debugName ?? "custom"),
                                     autoRebuildMode,
                                     isGlobalScaled));

    /// <summary>
    /// Unregister the UiBuilder. Do not call this in plugin code.
    /// </summary>
    void IDisposable.Dispose()
    {
        this.scopedFinalizer.Dispose();
    }

    /// <summary>Clean up resources allocated by this instance of <see cref="UiBuilder"/>.</summary>
    /// <remarks>Dalamud internal use only.</remarks>
    internal void DisposeInternal() => this.scopedFinalizer.Dispose();

    /// <summary>
    /// Open the registered configuration UI, if it exists.
    /// </summary>
    internal void OpenConfig()
    {
        this.OpenConfigUi?.InvokeSafely();
    }

    /// <summary>
    /// Open the registered configuration UI, if it exists.
    /// </summary>
    internal void OpenMain()
    {
        this.OpenMainUi?.InvokeSafely();
    }

    /// <summary>
    /// Notify this UiBuilder about plugin UI being hidden.
    /// </summary>
    internal void NotifyHideUi()
    {
        this.HideUi?.InvokeSafely();
    }

    /// <summary>
    /// Notify this UiBuilder about plugin UI being shown.
    /// </summary>
    internal void NotifyShowUi()
    {
        this.ShowUi?.InvokeSafely();
    }

    private void OnDraw()
    {
        this.hitchDetector.Start();

        var clientState = Service<ClientState>.Get();
        var gameGui = Service<GameGui>.GetNullable();
        if (gameGui == null)
            return;

        if ((gameGui.GameUiHidden && this.configuration.ToggleUiHide &&
             !(this.DisableUserUiHide || this.DisableAutomaticUiHide)) ||
            (this.CutsceneActive && this.configuration.ToggleUiHideDuringCutscenes &&
             !(this.DisableCutsceneUiHide || this.DisableAutomaticUiHide)) ||
            (clientState.IsGPosing && this.configuration.ToggleUiHideDuringGpose &&
             !(this.DisableGposeUiHide || this.DisableAutomaticUiHide)))
        {
            if (!this.lastFrameUiHideState)
            {
                this.lastFrameUiHideState = true;
                this.HideUi?.InvokeSafely();
            }

            return;
        }

        if (this.lastFrameUiHideState)
        {
            this.lastFrameUiHideState = false;
            this.ShowUi?.InvokeSafely();
        }

        // just in case, if something goes wrong, prevent drawing; otherwise it probably will crash.
        if (!this.FontAtlas.BuildTask.IsCompletedSuccessfully)
            return;

        ImGui.PushID(this.namespaceName);
        if (DoStats)
        {
            this.stopwatch.Restart();
        }

        if (this.hasErrorWindow && ImGui.Begin($"{this.namespaceName} Error", ref this.hasErrorWindow, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize))
        {
            ImGui.Text($"The plugin {this.namespaceName} ran into an error.\nContact the plugin developer for support.\n\nPlease try restarting your game.");
            ImGui.Spacing();

            if (ImGui.Button("OK"))
            {
                this.hasErrorWindow = false;
            }

            ImGui.End();
        }

        var snapshot = this.Draw is null ? null : ImGuiManagedAsserts.GetSnapshot();

        try
        {
            this.Draw?.InvokeSafely();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[{0}] UiBuilder OnBuildUi caught exception", this.namespaceName);
            this.Draw = null;
            this.OpenConfigUi = null;

            this.hasErrorWindow = true;
        }

        // Only if Draw was successful
        if (this.Draw is not null && snapshot is not null)
            ImGuiManagedAsserts.ReportProblems(this.namespaceName, snapshot);

        this.FrameCount++;

        if (DoStats)
        {
            this.stopwatch.Stop();
            this.LastDrawTime = this.stopwatch.ElapsedTicks;
            this.MaxDrawTime = Math.Max(this.LastDrawTime, this.MaxDrawTime);
            this.DrawTimeHistory.Add(this.LastDrawTime);
            while (this.DrawTimeHistory.Count > 100) this.DrawTimeHistory.RemoveAt(0);
        }

        ImGui.PopID();

        this.hitchDetector.Stop();
    }

    private void OnResizeBuffers()
    {
        this.ResizeBuffers?.InvokeSafely();
    }

    private class FontHandleWrapper : IFontHandle
    {
        private IFontHandle? wrapped;

        public FontHandleWrapper(IFontHandle wrapped)
        {
            this.wrapped = wrapped;
            this.wrapped.ImFontChanged += this.WrappedOnImFontChanged;
        }

        public event IFontHandle.ImFontChangedDelegate? ImFontChanged;

        public Exception? LoadException => this.WrappedNotDisposed.LoadException;

        public bool Available => this.WrappedNotDisposed.Available;

        private IFontHandle WrappedNotDisposed =>
            this.wrapped ?? throw new ObjectDisposedException(nameof(FontHandleWrapper));

        public void Dispose()
        {
            if (this.wrapped is not { } w)
                return;

            this.wrapped = null;
            w.ImFontChanged -= this.WrappedOnImFontChanged;
            // Note: do not dispose w; we do not own it
        }

        public ILockedImFont Lock() =>
            this.wrapped?.Lock() ?? throw new ObjectDisposedException(nameof(FontHandleWrapper));

        public IDisposable Push() => this.WrappedNotDisposed.Push();

        public void Pop() => this.WrappedNotDisposed.Pop();

        public Task<IFontHandle> WaitAsync() =>
            this.WrappedNotDisposed.WaitAsync().ContinueWith(_ => (IFontHandle)this);

        public override string ToString() =>
            $"{nameof(FontHandleWrapper)}({this.wrapped?.ToString() ?? "disposed"})";

        private void WrappedOnImFontChanged(IFontHandle obj, ILockedImFont lockedFont) =>
            this.ImFontChanged?.Invoke(obj, lockedFont);
    }
}
