using System;
using System.Collections.Generic;

namespace SETUNA.Main.Window
{
    /// <summary>
    /// Semantic DPI policy for a top-level window.
    /// Logical UI windows let WinForms scale their designer metrics; physical
    /// surfaces keep bitmap and canvas coordinates in device pixels.
    /// </summary>
    public enum DpiPolicy
    {
        LogicalUi,
        PhysicalSurface
    }

    /// <summary>
    /// Explicit inventory of every BaseForm-derived window. Exact-type lookup is
    /// intentional: adding a new form cannot silently inherit the wrong policy.
    /// </summary>
    public static class DpiPolicyRegistry
    {
        static readonly IReadOnlyDictionary<Type, DpiPolicy> Policies =
            new Dictionary<Type, DpiPolicy>
            {
                [typeof(global::SETUNA.Mainform)] = DpiPolicy.LogicalUi,
                [typeof(global::SETUNA.ClickCapture)] = DpiPolicy.PhysicalSurface,
                [typeof(global::SETUNA.Main.CaptureForm)] = DpiPolicy.PhysicalSurface,
                [typeof(global::SETUNA.Main.CaptureInfo)] = DpiPolicy.PhysicalSurface,
                [typeof(global::SETUNA.Main.CaptureSelLine)] = DpiPolicy.PhysicalSurface,
                [typeof(global::SETUNA.Main.HotkeyMsg)] = DpiPolicy.LogicalUi,
                [typeof(global::SETUNA.Main.Magnifier)] = DpiPolicy.PhysicalSurface,
                [typeof(global::SETUNA.Main.Option.OptionForm)] = DpiPolicy.LogicalUi,
                [typeof(global::SETUNA.Main.Option.StyleEditForm)] = DpiPolicy.LogicalUi,
                [typeof(global::SETUNA.Main.ScrapBase)] = DpiPolicy.PhysicalSurface,
                [typeof(global::SETUNA.Main.SplashForm)] = DpiPolicy.LogicalUi,
                [typeof(global::SETUNA.Main.StyleItems.CompactScrap)] = DpiPolicy.PhysicalSurface,
                [typeof(global::SETUNA.Main.StyleItems.CompactStyleItemPanel)] = DpiPolicy.LogicalUi,
                [typeof(global::SETUNA.Main.StyleItems.CopyStyleItemPanel)] = DpiPolicy.LogicalUi,
                [typeof(global::SETUNA.Main.StyleItems.ImageBmpStyleItemPanel)] = DpiPolicy.LogicalUi,
                [typeof(global::SETUNA.Main.StyleItems.ImageJpegPreviewPanel)] = DpiPolicy.LogicalUi,
                [typeof(global::SETUNA.Main.StyleItems.ImageJpegStyleItemPanel)] = DpiPolicy.LogicalUi,
                [typeof(global::SETUNA.Main.StyleItems.ImagePngPreviewPanel)] = DpiPolicy.LogicalUi,
                [typeof(global::SETUNA.Main.StyleItems.ImagePngStyleItemPanel)] = DpiPolicy.LogicalUi,
                [typeof(global::SETUNA.Main.StyleItems.LayerRenameWindow)] = DpiPolicy.LogicalUi,
                [typeof(global::SETUNA.Main.StyleItems.LoginInput)] = DpiPolicy.LogicalUi,
                [typeof(global::SETUNA.Main.StyleItems.MarginStyleItemPanel)] = DpiPolicy.LogicalUi,
                [typeof(global::SETUNA.Main.StyleItems.MoveStyleItemPanel)] = DpiPolicy.LogicalUi,
                [typeof(global::SETUNA.Main.StyleItems.NothingStyleItemPanel)] = DpiPolicy.LogicalUi,
                [typeof(global::SETUNA.Main.StyleItems.OpacityStyleItemPanel)] = DpiPolicy.LogicalUi,
                [typeof(global::SETUNA.Main.StyleItems.PaintForm)] = DpiPolicy.PhysicalSurface,
                [typeof(global::SETUNA.Main.StyleItems.PicasaBar)] = DpiPolicy.LogicalUi,
                [typeof(global::SETUNA.Main.StyleItems.PicasaStyleItemPanel)] = DpiPolicy.LogicalUi,
                [typeof(global::SETUNA.Main.StyleItems.RotateStyleItemPanel)] = DpiPolicy.LogicalUi,
                [typeof(global::SETUNA.Main.StyleItems.SaveImageStyleItemPanel)] = DpiPolicy.LogicalUi,
                [typeof(global::SETUNA.Main.StyleItems.ScaleStyleItemPanel)] = DpiPolicy.LogicalUi,
                [typeof(global::SETUNA.Main.StyleItems.ScrapDrawForm)] = DpiPolicy.PhysicalSurface,
                [typeof(global::SETUNA.Main.StyleItems.ScrapPaintLayer)] = DpiPolicy.LogicalUi,
                [typeof(global::SETUNA.Main.StyleItems.ScrapPaintPenTool)] = DpiPolicy.LogicalUi,
                [typeof(global::SETUNA.Main.StyleItems.ScrapPaintTextTool)] = DpiPolicy.LogicalUi,
                [typeof(global::SETUNA.Main.StyleItems.ScrapPaintToolBar)] = DpiPolicy.LogicalUi,
                [typeof(global::SETUNA.Main.StyleItems.ScrapPaintWindow)] = DpiPolicy.PhysicalSurface,
                [typeof(global::SETUNA.Main.StyleItems.TimerStyleItemPanel)] = DpiPolicy.LogicalUi,
                [typeof(global::SETUNA.Main.StyleItems.ToolBoxForm)] = DpiPolicy.LogicalUi,
                [typeof(global::SETUNA.Main.StyleItems.TrimWindow)] = DpiPolicy.PhysicalSurface,
                [typeof(global::SETUNA.Main.StyleItems.WindowStyleItemPanel)] = DpiPolicy.LogicalUi
            };

        public static DpiPolicy GetPolicy(Type formType)
        {
            if (formType == null)
            {
                throw new ArgumentNullException(nameof(formType));
            }

            if (!Policies.TryGetValue(formType, out var policy))
            {
                throw new InvalidOperationException(
                    $"Form '{formType.FullName}' has no explicit DPI policy classification.");
            }

            return policy;
        }

        public static bool TryGetPolicy(Type formType, out DpiPolicy policy)
        {
            if (formType == null)
            {
                policy = default(DpiPolicy);
                return false;
            }

            return Policies.TryGetValue(formType, out policy);
        }
    }
}
