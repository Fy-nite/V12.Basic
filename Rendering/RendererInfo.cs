using System;
using System.Collections.Generic;
using System.Text;
using V12;

namespace V12.Basic.Rendering
{
    /// <summary>
    /// Provides information about what kind of renderer the implementer is.
    /// usually you read information from this to figure out what kind of features the renderer supports,
    /// the basic information like name, description, version and more.
    /// 
    /// if you're doing checks against what kind of renderer features are availabe, use the <see cref="RendererLoader"/>
    /// </summary>
    public abstract class Rendererinfo
    {
        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract Feature[] Features { get; }

        /// <summary>
        /// Boot this renderer and run the supplied game pak. Blocks for as long as
        /// the renderer runs.
        /// </summary>
        public abstract void StartRenderer(IV12Gamepack pak);
    }
}
