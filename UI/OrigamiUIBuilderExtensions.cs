using System;
using System.Collections.Generic;
using V12.Core.Core.Interfaces;
using V12.Basic.Components.Origami;
using V12.Components.UI;

namespace V12.Basic.UI
{
    /// <summary>
    /// <see cref="V12.Core.UI.UIBuilder"/> helpers for Origami widgets.
    /// Keeps the engine-level <see cref="V12.Core.UI.IUIBuilder"/> stable (Paper
    /// mapping stays in <see cref="V12.Core.GameRoot"/>) while exposing the Origami
    /// widget set (including the code editor) in the <c>V12.Basic</c> namespace.
    /// All widgets are world-elements: the <c>ProwlPaperRenderer</c> maps them to
    /// Paper/Origami meshes; Godot fallback renders them as plain containers so
    /// layouts still work.
    /// </summary>
    public static class OrigamiUIBuilderExtensions
    {
        public static IWorldElement CodeEditor(this V12.Core.UI.UIBuilder builder, IWorldElement parent, string name, string text, string language = "csharp", Action<string>? onChanged = null)
        {
            var el = new V12.Core.Element(name);
            el.Components.Add(new CodeEditorComponent(text, language){ OnChanged = onChanged });
            parent.AddChild(el);
            return el;
        }

        public static IWorldElement DockSpace(this V12.Core.UI.UIBuilder builder, IWorldElement parent, string name, string spaceId = "dock", bool allowFloating = true, float padding = 0f)
        {
            var el = new V12.Core.Element(name);
            el.Components.Add(new DockSpaceComponent
            {
                DockSpaceId = spaceId,
                AllowFloating = allowFloating,
                DockPadding = padding,
            });
            parent.AddChild(el);
            return el;
        }

        public static IWorldElement DockPanel(this V12.Core.UI.UIBuilder builder, IWorldElement dockSpace, string name, string title, DockSplit split = DockSplit.Auto, float ratio = 0.5f, bool closable = true, bool floating = false)
        {
            var el = new V12.Core.Element(name);
            el.Components.Add(new DockPanelComponent
            {
                Title = title,
                Split = split,
                SplitRatio = ratio,
                Closable = closable,
                Floating = floating,
            });
            dockSpace.AddChild(el);
            return el;
        }

        public static IWorldElement PropertyGrid(this V12.Core.UI.UIBuilder builder, IWorldElement parent, string name, long targetElementId = 0)
        {
            var el = new V12.Core.Element(name);
            el.Components.Add(new PropertyGridComponent { TargetElementId = targetElementId });
            parent.AddChild(el);
            return el;
        }

        public static IWorldElement Tabs(this V12.Core.UI.UIBuilder builder, IWorldElement parent, string name, IEnumerable<string> tabs, int selected = 0, Action<int>? onSelected = null)
        {
            var el = new V12.Core.Element(name);
            el.Components.Add(new TabsComponent
            {
                Tabs = new List<string>(tabs),
                SelectedIndex = selected,
                OnSelected = onSelected,
            });
            parent.AddChild(el);
            return el;
        }

        public static IWorldElement TabPage(this V12.Core.UI.UIBuilder builder, IWorldElement tabs, string name, int index, string title = "")
        {
            var el = new V12.Core.Element(name);
            el.Components.Add(new TabPageComponent { Index = index, Title = title });
            tabs.AddChild(el);
            return el;
        }

        public static IWorldElement Table(this V12.Core.UI.UIBuilder builder, IWorldElement parent, string name, IEnumerable<string> columns, Action<int>? onSelected = null)
        {
            var el = new V12.Core.Element(name);
            var table = new TableComponent { OnSelected = onSelected };
            foreach (var column in columns)
                table.Columns.Add(new UITableColumn { Header = column });
            el.Components.Add(table);
            parent.AddChild(el);
            return el;
        }

        public static IWorldElement Dropdown(this V12.Core.UI.UIBuilder builder, IWorldElement parent, string name, IEnumerable<string> items, int selected = 0, Action<int>? onSelected = null)
        {
            var el = new V12.Core.Element(name);
            el.Components.Add(new DropdownComponent
            {
                Items = new List<string>(items),
                SelectedIndex = selected,
                OnSelected = onSelected,
            });
            parent.AddChild(el);
            return el;
        }

        public static IWorldElement Separator(this V12.Core.UI.UIBuilder builder, IWorldElement parent, string name)
        {
            var el = new V12.Core.Element(name);
            el.Components.Add(new SeparatorComponent());
            parent.AddChild(el);
            return el;
        }

        public static IWorldElement Spinner(this V12.Core.UI.UIBuilder builder, IWorldElement parent, string name, float size = 0f)
        {
            var el = new V12.Core.Element(name);
            el.Components.Add(new SpinnerComponent { Size = size });
            parent.AddChild(el);
            return el;
        }
    }
}
