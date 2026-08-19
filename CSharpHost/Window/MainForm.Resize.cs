using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SleepyChat;

internal sealed partial class MainForm : Form, IMessageFilter
{
    public bool PreFilterMessage(ref Message m)
    {
        if (shutdownStarted || WindowState != FormWindowState.Normal)
            return false;

        if (manualResizeActive)
        {
            if (m.Msg == WM_MOUSEMOVE)
            {
                ApplyManualResize(Cursor.Position);
                return true;
            }

            if (m.Msg == WM_LBUTTONUP)
            {
                EndManualResize();
                return true;
            }

            return false;
        }

        if (m.Msg is not (WM_SETCURSOR or WM_LBUTTONDOWN))
            return false;

        var direction = HitTestResizeDirection(Cursor.Position);
        if (direction == ResizeDirection.None)
            return false;

        Cursor.Current = CursorForResizeDirection(direction);
        if (m.Msg == WM_LBUTTONDOWN)
        {
            BeginManualResize(direction);
            return true;
        }

        return true;
    }

    private ResizeDirection HitTestResizeDirection(Point screenPoint)
    {
        var bounds = Bounds;
        if (!bounds.Contains(screenPoint))
            return ResizeDirection.None;

        var x = screenPoint.X - bounds.Left;
        var y = screenPoint.Y - bounds.Top;
        var width = bounds.Width;
        var height = bounds.Height;

        var nearLeftCorner = x < ResizeCornerSize;
        var nearRightCorner = x >= width - ResizeCornerSize;
        var nearTopCorner = y < ResizeCornerSize;
        var nearBottomCorner = y >= height - ResizeCornerSize;

        if (nearTopCorner && nearLeftCorner) return ResizeDirection.TopLeft;
        if (nearTopCorner && nearRightCorner) return ResizeDirection.TopRight;
        if (nearBottomCorner && nearLeftCorner) return ResizeDirection.BottomLeft;
        if (nearBottomCorner && nearRightCorner) return ResizeDirection.BottomRight;

        if (x < ResizeGripThickness) return ResizeDirection.Left;
        if (x >= width - ResizeGripThickness) return ResizeDirection.Right;
        if (y < ResizeGripThickness) return ResizeDirection.Top;
        if (y >= height - ResizeGripThickness) return ResizeDirection.Bottom;

        return ResizeDirection.None;
    }

    private static Cursor CursorForResizeDirection(ResizeDirection direction) => direction switch
    {
        ResizeDirection.Top or ResizeDirection.Bottom => Cursors.SizeNS,
        ResizeDirection.Left or ResizeDirection.Right => Cursors.SizeWE,
        ResizeDirection.TopLeft or ResizeDirection.BottomRight => Cursors.SizeNWSE,
        ResizeDirection.TopRight or ResizeDirection.BottomLeft => Cursors.SizeNESW,
        _ => Cursors.Default
    };

    private void BeginManualResize(ResizeDirection direction)
    {
        if (WindowState != FormWindowState.Normal)
            return;

        manualResizeActive = true;
        manualResizeDirection = direction;
        manualResizeStartCursor = Cursor.Position;
        manualResizeStartBounds = Bounds;
        Capture = true;
        resizeTimer.Start();
    }

    private void ApplyManualResize(Point cursorPosition)
    {
        var dx = cursorPosition.X - manualResizeStartCursor.X;
        var dy = cursorPosition.Y - manualResizeStartCursor.Y;
        var start = manualResizeStartBounds;
        var left = start.Left;
        var top = start.Top;
        var width = start.Width;
        var height = start.Height;

        var resizeLeft = manualResizeDirection is ResizeDirection.Left or ResizeDirection.TopLeft or ResizeDirection.BottomLeft;
        var resizeRight = manualResizeDirection is ResizeDirection.Right or ResizeDirection.TopRight or ResizeDirection.BottomRight;
        var resizeTop = manualResizeDirection is ResizeDirection.Top or ResizeDirection.TopLeft or ResizeDirection.TopRight;
        var resizeBottom = manualResizeDirection is ResizeDirection.Bottom or ResizeDirection.BottomLeft or ResizeDirection.BottomRight;

        if (resizeLeft)
        {
            left = start.Left + dx;
            width = start.Width - dx;
        }
        else if (resizeRight)
        {
            width = start.Width + dx;
        }

        if (resizeTop)
        {
            top = start.Top + dy;
            height = start.Height - dy;
        }
        else if (resizeBottom)
        {
            height = start.Height + dy;
        }

        var minWidth = Math.Max(1, MinimumSize.Width);
        var minHeight = Math.Max(1, MinimumSize.Height);
        if (width < minWidth)
        {
            width = minWidth;
            if (resizeLeft)
                left = start.Right - minWidth;
        }

        if (height < minHeight)
        {
            height = minHeight;
            if (resizeTop)
                top = start.Bottom - minHeight;
        }

        SetBounds(left, top, width, height);
    }

    private void EndManualResize()
    {
        if (!manualResizeActive)
            return;

        manualResizeActive = false;
        resizeTimer.Stop();
        Capture = false;
    }

    private enum ResizeDirection
    {
        None,
        Top,
        Bottom,
        Left,
        Right,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }
}
