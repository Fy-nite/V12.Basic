using V12.Components;
using V12.Core.Core.Interfaces;

namespace V12.Basic.Components.Origami
{
    /// <summary>How a <see cref="DockPanelComponent"/> joins the dock tree.</summary>
    public enum DockSplit
    {
        /// <summary>Join the previous panel as a tab in the same leaf.</summary>
        Auto = 0,
        /// <summary>Split the current group side-by-side (new panel on the right).</summary>
        Horizontal = 1,
        /// <summary>Split the current group top/bottom (new panel below).</summary>
        Vertical = 2,
    }

    /// <summary>
    /// Origami dock space: a tree of draggable, resizable, tabbed panels
    /// (<c>Prowl.OrigamiUI.DockSpace</c>). The element's <see cref="DockPanelComponent"/>
    /// children define the tree:
    ///
    /// <code>
    /// DockSpace
    ///   DockPanel "Hierarchy"
    ///   DockPanel "Viewport"  Split=Horizontal Ratio=0.75
    ///   DockPanel "Inspector" Split=Vertical   Ratio=0.7
    /// </code>
    ///
    /// Panels with <see cref="DockSplit.Auto"/> become tabs of the previous leaf.
    /// Panels with <see cref="DockPanelComponent.Floating"/> pop out into their own window.
    /// A panel's own element children render inside its body.
    /// </summary>
    public class DockSpaceComponent : ComponentBase
    {
        public override string Name => "DockSpace";
        public override string Description => "Origami dock space (draggable, tabbed, splittable panels)";

        /// <summary>Stable id used to keep the dock layout across frames / saves.</summary>
        public string DockSpaceId { get; set; } = "dock";

        /// <summary>Allow the user to drag splitters and re-dock tabs.</summary>
        public bool AllowSplitting { get; set; } = true;

        /// <summary>Allow tabs to be torn out into floating windows.</summary>
        public bool AllowFloating { get; set; } = true;

        /// <summary>Inset applied around the dock area, in pixels.</summary>
        public float DockPadding { get; set; } = 0f;

        /// <summary>Dock area in pixels. 0 = fill the canvas.</summary>
        public float Width { get; set; }
        public float Height { get; set; }

        /// <summary>Optional serialized layout (see <c>DockSerializer</c>); applied on first frame.</summary>
        public string LayoutJson { get; set; } = "";

        public override IWorldElement BuildUI() => new V12.Core.Element();
    }

    /// <summary>
    /// One panel inside a <see cref="DockSpaceComponent"/>. Its element children are
    /// rendered into the panel body by the Paper/Origami renderer.
    /// </summary>
    public class DockPanelComponent : ComponentBase
    {
        public override string Name => "DockPanel";
        public override string Description => "Panel inside an Origami dock space";

        /// <summary>Tab caption.</summary>
        public string Title { get; set; } = "Panel";

        /// <summary>Id of the owning dock space (matches <see cref="DockSpaceComponent.Id"/>).</summary>
        public string DockId { get; set; } = "dock";

        /// <summary>Optional glyph key (Origami icon set).</summary>
        public string Icon { get; set; } = "";

        /// <summary>Whether the tab shows a close button.</summary>
        public bool Closable { get; set; } = true;

        /// <summary>Pop this panel out into a floating window instead of docking it.</summary>
        public bool Floating { get; set; }

        /// <summary>How this panel joins the dock tree relative to the previous one.</summary>
        public DockSplit Split { get; set; } = DockSplit.Auto;

        /// <summary>Split share of the previous group (0..1) when <see cref="Split"/> is not Auto.</summary>
        public float SplitRatio { get; set; } = 0.5f;

        /// <summary>Floating window rect, in pixels (used when <see cref="Floating"/>).</summary>
        public float FloatingX { get; set; } = 80f;
        public float FloatingY { get; set; } = 80f;
        public float FloatingWidth { get; set; } = 520f;
        public float FloatingHeight { get; set; } = 360f;

        public override IWorldElement BuildUI() => new V12.Core.Element();
    }
}

