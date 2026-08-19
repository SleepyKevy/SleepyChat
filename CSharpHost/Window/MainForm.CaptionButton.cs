using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SleepyChat;

internal sealed partial class MainForm : Form, IMessageFilter
{
    private enum CaptionButtonKind
    {
        Minimize,
        Maximize,
        Close
    }

    private sealed class CaptionButton : Button
    {
        private readonly CaptionButtonKind kind;
        private bool restoreGlyph;

        public CaptionButton(CaptionButtonKind kind)
        {
            this.kind = kind;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseDownBackColor = kind == CaptionButtonKind.Close
                ? Color.FromArgb(170, 35, 31)
                : Color.FromArgb(25, 42, 63);
            FlatAppearance.MouseOverBackColor = kind == CaptionButtonKind.Close
                ? Color.FromArgb(196, 43, 35)
                : Color.FromArgb(18, 36, 58);
            BackColor = Color.FromArgb(6, 8, 13);
            ForeColor = Color.FromArgb(222, 226, 234);
            UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        public bool RestoreGlyph
        {
            get => restoreGlyph;
            set
            {
                if (restoreGlyph == value)
                    return;
                restoreGlyph = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using var pen = new Pen(ForeColor, 1.15F)
            {
                StartCap = LineCap.Square,
                EndCap = LineCap.Square
            };

            var cx = ClientSize.Width / 2F;
            var cy = ClientSize.Height / 2F;

            switch (kind)
            {
                case CaptionButtonKind.Minimize:
                    e.Graphics.DrawLine(pen, cx - 5F, cy + 3F, cx + 5F, cy + 3F);
                    break;

                case CaptionButtonKind.Maximize:
                    if (!restoreGlyph)
                    {
                        e.Graphics.DrawRectangle(pen, cx - 4.5F, cy - 4.5F, 9F, 9F);
                    }
                    else
                    {
                        e.Graphics.DrawRectangle(pen, cx - 5F, cy - 2.5F, 8F, 8F);
                        e.Graphics.DrawLine(pen, cx - 2F, cy - 5F, cx + 5F, cy - 5F);
                        e.Graphics.DrawLine(pen, cx + 5F, cy - 5F, cx + 5F, cy + 2F);
                        e.Graphics.DrawLine(pen, cx + 3F, cy - 3F, cx + 3F, cy + 2F);
                    }
                    break;

                case CaptionButtonKind.Close:
                    e.Graphics.DrawLine(pen, cx - 4.5F, cy - 4.5F, cx + 4.5F, cy + 4.5F);
                    e.Graphics.DrawLine(pen, cx + 4.5F, cy - 4.5F, cx - 4.5F, cy + 4.5F);
                    break;
            }
        }
    }
}
