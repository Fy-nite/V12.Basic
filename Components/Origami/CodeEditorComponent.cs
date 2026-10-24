using System;
using V12.Components;
using V12.Core.Core.Interfaces;

namespace V12.Basic.Components.Origami
{
    /// <summary>
    /// Origami CodeEditor widget mapped from <c>Prowl.OrigamiUI.CodeEditor</c>.
    /// Holds a <see cref="CodeDocument"/>-backed text buffer, language, and diagnostics.
    /// Frontends that support Paper/Origami (ProwlPaperRenderer) render this via
    /// Origami.CodeEditor; Godot fallback renders as a read-only TextInput.
    /// </summary>
    public class CodeEditorComponent : ComponentBase
    {
        public override string Name => "CodeEditor";
        public override string Description => "Origami code editor (syntax-highlighted, diagnostics, completion)";

        private string _text = string.Empty;
        private string _language = "csharp";
        private bool _readOnly;
        private bool _showLineNumbers = true;
        private bool _wordWrap;
        private int _tabSize = 4;

        /// <summary>Source text.</summary>
        public string Text
        {
            get => _text;
            set { if (_text != value) { _text = value ?? string.Empty; MarkDirty(); } }
        }

        /// <summary>Language id: csharp, hlsl, glsl, json, xml, contract, etc.</summary>
        public string Language
        {
            get => _language;
            set { if (_language != value) { _language = value ?? "csharp"; MarkDirty(); } }
        }

        public bool ReadOnly { get => _readOnly; set { if (_readOnly != value) { _readOnly = value; MarkDirty(); } } }
        public bool ShowLineNumbers { get => _showLineNumbers; set { if (_showLineNumbers != value) { _showLineNumbers = value; MarkDirty(); } } }
        public bool WordWrap { get => _wordWrap; set { if (_wordWrap != value) { _wordWrap = value; MarkDirty(); } } }
        public int TabSize { get => _tabSize; set { var v = Math.Clamp(value, 1, 8); if (_tabSize != v) { _tabSize = v; MarkDirty(); } } }

        /// <summary>Raised when user commits text (frontend invokes).</summary>
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        public Action<string>? OnChanged { get; set; }

        public CodeEditorComponent() { }
        public CodeEditorComponent(string text, string language = "csharp") { _text = text ?? ""; _language = language; }

        public void InvokeChanged(string newText) { _text = newText ?? ""; try { OnChanged?.Invoke(_text); } catch { } MarkDirty(); }

        public override IWorldElement BuildUI() => new V12.Core.Element();
    }
}
