using System;
using V12.UI;

namespace V12.Basic.UI
{
    /// <summary>
    /// Renderer-facing services the Prowl editor shell needs but Paper cannot own:
    /// a 3D viewport texture, native file dialogs, modal dialogs, and window handles.
    /// Engines/hosts implement this; the provider itself stays renderer-agnostic.
    /// </summary>
    public interface IProwlHost
    {
        object? GameViewTexture { get; }
        void SetGameViewRect(global::Prowl.Vector.Rect rect);
        IFilePicker? CreateFilePicker();
        IModal? CreateModal(string title, string content);
        IWindow? CreateWindow(string title, WindowOptions options);
    }

    public sealed class NullProwlHost : IProwlHost
    {
        public static readonly NullProwlHost Instance = new();
        public object? GameViewTexture => null;
        public void SetGameViewRect(global::Prowl.Vector.Rect rect) { }
        public IFilePicker? CreateFilePicker() => null;
        public IModal? CreateModal(string title, string content) => null;
        public IWindow? CreateWindow(string title, WindowOptions options) => null;
    }

    public sealed class NullFilePicker : IFilePicker
    {
        public void ShowOpen(string title, string description, string[] extensions, Action<string?> onResult) => onResult(null);
        public void ShowFolder(string title, Action<string?> onResult) => onResult(null);
        public void ShowSave(string title, string description, string[] extensions, string suggestedFileName, Action<string?> onResult) => onResult(null);
        public void Cancel() { }
    }

    public sealed class NullProwlWindow : IWindow
    {
        public object NativeControl { get; } = new object();
        public bool Visible { get; set; } = true;
        public string Title { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public void Close() => Visible = false;
    }

    public sealed class NullProwlModal : IModal
    {
        public object NativeControl { get; } = new object();
        public bool Visible { get; set; } = true;
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
        public void Show() => Visible = true;
        public void Close() => Visible = false;
    }

    internal sealed class ProwlViewportHost : IViewportHost
    {
        public object NativeControl { get; } = new object();
        public bool Visible { get; set; } = true;
        public void SetRenderTarget(V12.Rendering.IRenderTarget target) { }
        public void ClearRenderTarget() { }
    }
}


