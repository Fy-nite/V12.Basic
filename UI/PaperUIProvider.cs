using System;
using System.Collections.Generic;
using System.Linq;
using V12.Basic.Components.Origami;
using V12.Components;
using V12.Components.UI;
using V12.Core;
using V12.UI;
using V12.UI.Paper;

namespace V12.Basic.UI
{
    internal static class PaperDirty
    {
        public static void Mark() => V12.Core.GameRoot.Instance?.MarkRenderDirty();
    }

    /// <summary>
    /// V12.Basic's default UI provider: the core PaperUIProvider plus the
    /// Origami-backed controls and a dock-space surface.
    /// </summary>
    public class PaperUIProvider : V12.UI.Paper.PaperUIProvider, IDockProvider
    {
        public PaperUIProvider(GameRoot root) : base(root) => ApplyTheme();

        private static void ApplyTheme()
        {
            var hex = V12.Core.UI.Paper.PaperPalette.FromHex;
            V12.Core.UI.Paper.PaperPalette.Set(
                hex("#151a24"), hex("#1d2330"), hex("#232b3a"), hex("#2b3547"),
                hex("#334561"), hex("#2c3647"), hex("#4f9ad8"), hex("#dbe4f1"),
                hex("#8b97aa"), hex("#5c6678"), false,
                hex("#e06c75"), hex("#7fc48b"));
            var theme = global::Prowl.OrigamiUI.Origami.Root;
            theme.Metrics.Rounding = 0f;
            theme.Metrics.ContainerRounding = 0f;
            theme.Metrics.SmallRounding = 0f;
        }

        public override ICodeEditor CreateCodeEditor() => new PaperOrigamiCodeEditor(this);

        public IDockSpaceWidget CreateDockSpace() => new PaperDockSpaceWidget(this);
    }

    public sealed class PaperOrigamiCodeEditor : PaperWidget, ICodeEditor
    {
        private readonly CodeEditorComponent _editor;
        public PaperOrigamiCodeEditor(PaperUIProvider owner) : base(owner, "CodeEditor")
        {
            _editor = new CodeEditorComponent();
            Element.AddComponent(_editor);
        }

        public string Text { get => _editor.Text; set { _editor.Text = value ?? ""; PaperDirty.Mark(); } }
        public string FilePath { get; set; } = "";
        public bool ReadOnly { get => _editor.ReadOnly; set { _editor.ReadOnly = value; PaperDirty.Mark(); } }
        public void SetLanguage(string languageId) { _editor.Language = languageId ?? "csharp"; PaperDirty.Mark(); }
        public void SetCompilerLanguage(global::Prowl.OrigamiUI.CodeLanguage language) { }
        public void SetOnChanged(Action<string> onChanged) => _editor.OnChanged = onChanged;
        public void SetOnSaveRequested(Action onSave) { }
        public void Focus() { }
    }

    public sealed class PaperDockSpaceWidget : PaperWidget, IDockSpaceWidget
    {
        private sealed class Leaf : IDockNode
        {
            public readonly List<DockTab> Tabs = new();
            public Leaf(IEnumerable<DockTab> tabs) => Tabs.AddRange(tabs);
        }

        private sealed class SplitNode : IDockNode
        {
            public DockOrientation Orientation;
            public float Ratio;
            public IDockNode First = null!;
            public IDockNode Second = null!;
        }

        private readonly List<DockTab> _allTabs = new();
        private readonly DockSpaceComponent _dock;

        public PaperDockSpaceWidget(PaperUIProvider owner) : base(owner, "DockSpace")
        {
            _dock = new DockSpaceComponent
            {
                DockSpaceId = "studio",
                AllowSplitting = true,
                AllowFloating = false,
                DockPadding = 36f,
            };
            Element.AddComponent(_dock);
        }

        public IDockNode Tabs(params DockTab[] tabs) => new Leaf(tabs ?? Array.Empty<DockTab>());

        public IDockNode Split(DockOrientation orientation, float ratio, IDockNode first, IDockNode second)
            => new SplitNode { Orientation = orientation, Ratio = ratio, First = first, Second = second };

        public void SetRoot(IDockNode root)
        {
            foreach (var child in Element.Children.ToArray()) Element.RemoveChild(child);
            _allTabs.Clear();
            Collect(root);
            foreach (var tab in OrderTabs(_allTabs))
                AddPanel(tab);
            PaperDirty.Mark();
        }

        private void Collect(IDockNode node)
        {
            switch (node)
            {
                case Leaf leaf: _allTabs.AddRange(leaf.Tabs); break;
                case SplitNode split: Collect(split.First); Collect(split.Second); break;
            }
        }

        private static IEnumerable<DockTab> OrderTabs(IEnumerable<DockTab> tabs)
        {
            int Rank(DockTab t) => (t.Title ?? "").ToLowerInvariant() switch
            {
                "hierarchy" => 0,
                "viewport" => 1,
                "inspector" => 2,
                "project" => 3,
                "console" => 4,
                "packs" => 5,
                "code" => 6,
                _ => 10,
            };
            return tabs.OrderBy(Rank);
        }

        private void AddPanel(DockTab tab)
        {
            var panel = new Element("DockPanel_" + tab.Title);
            var split = DockSplit.Auto;
            float ratio = 0.5f;
            switch ((tab.Title ?? "").ToLowerInvariant())
            {
                case "viewport": split = DockSplit.Horizontal; ratio = 0.18f; break;
                case "inspector": split = DockSplit.Horizontal; ratio = 0.80f; break;
                case "project": split = DockSplit.Vertical; ratio = 0.72f; break;
            }
            panel.AddComponent(new DockPanelComponent
            {
                Title = tab.Title ?? "Panel",
                Split = split,
                SplitRatio = ratio,
                Closable = false,
            });
            if (tab.Content is PaperWidget widget) panel.AddChild(widget.Element);
            Element.AddChild(panel);
        }

        public void ShowTab(string title)
        {
            _dock.ActiveTab = title ?? "";
            PaperDirty.Mark();
        }
    }
}

