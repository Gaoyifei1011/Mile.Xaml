﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mile.Xaml.Interop;
using Windows.UI.Core;

namespace Mile.Xaml
{
    /// <summary>
    ///     WindowsXamlHostBase hosts UWP XAML content inside Windows Forms
    /// </summary>
    public partial class WindowsXamlHostBase
    {
        /// <summary>
        /// Draw a placeholder Rectangle with 'Xaml Content' in Design mode
        /// </summary>
        /// <param name="e">PaintEventArgs</param>
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected override void OnPaint(PaintEventArgs e)
        {
            // Show 'XAML Content' with a gray Rectangle placeholder when running in the Designer
            if (DesignMode)
            {
                var graphics = e.Graphics;

                // Gray background Rectangle
                graphics.FillRectangle(new SolidBrush(Color.DarkGray), ClientRectangle);

                // 'XAML Content' text
                var text1 = "XAML Content";
                using (var font1 = new Font("Arial", 12, FontStyle.Bold, GraphicsUnit.Point))
                {
                    var rect1 = ClientRectangle;

                    var stringFormat = new StringFormat();
                    stringFormat.Alignment = StringAlignment.Center;
                    stringFormat.LineAlignment = StringAlignment.Center;
                    e.Graphics.DrawString(text1, font1, Brushes.White, rect1, stringFormat);
                    e.Graphics.DrawRectangle(Pens.Black, rect1);
                }

                return;
            }

            base.OnPaint(e);
        }

        /// <summary>
        /// Prevent control from painting the background
        /// </summary>
        /// <param name="pevent">PaintEventArgs</param>
        [Browsable(false)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            // Do not draw the background
        }

#if NET8_0_OR_GREATER
        [LibraryImport("user32.dll", EntryPoint = "SendMessageW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        internal static partial IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
#else
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
#endif

        /// <summary>
        /// Processes Windows messages for XamlContentHost control window (not XAML window)
        /// </summary>
        /// <param name="m">message to process</param>
        protected override void WndProc(ref Message m)
        {
            if (DesignMode)
            {
                base.WndProc(ref m);
            }

            switch (m.Msg)
            {
                // SetDesktopWindowXamlSourceWindowPos must always be called after base.WndProc
                case NativeDefines.WM_MOVE:
                case NativeDefines.WM_SIZE:
                case NativeDefines.WM_WINDOWPOSCHANGED:
                    base.WndProc(ref m);
                    SetDesktopWindowXamlSourceWindowPos();

                    IntPtr CoreWindowHandle =
                        CoreWindow.GetForCurrentThread().GetInterop().GetWindowHandle();
                    Message CurrentMessage = m;

                    // Use Delay execution for improving the resizing for ContentDialogs.
                    Task.Delay(200).ContinueWith(t =>
                    {
                        // Reference: https://github.com/microsoft/microsoft-ui-xaml
                        //            /issues/3577
                        // ContentDialogs don't resize themselves when the XAML island
                        // resizes. However, if we manually resize our CoreWindow, that'll
                        // actually trigger a resize of the ContentDialog.
                        SendMessage(
                            CoreWindowHandle,
                            CurrentMessage.Msg,
                            CurrentMessage.WParam,
                            CurrentMessage.LParam);
                    });

                   
                    break;

                // BUGBUG: Focus integration with Windows.UI.Xaml.Hosting.XamlSourceFocusNavigation is
                // skipping over nested elements. Update or move back to Windows.Xaml.Input.FocusManager.
                // WM_SETFOCUS should not be handled directly. MS Internal: DesktopWindowXamlSource.NavigateFocus
                // non-directional Focus not moving Focus, not responding to keyboard input.
                case NativeDefines.WM_SETFOCUS:
                    // BUGBUG: Work-around internal aggressive FAILFAST bug.  Remove this when #19043466 or nested element support is fixed.
                    if (m.WParam != m.HWnd)
                    {
                        // Temporarily drop some WM_SETFOCUS messages to prevent calling Focus on Focused element.  An
                        // unnecessary Focus operation may trigger a FAILFAST inside UWP XAML's DesktopWindowXamlSource.
                        return;
                    }

                    if (UnsafeNativeMethods.IntSetFocus(_xamlIslandWindowHandle) == System.IntPtr.Zero)
                    {
                        throw new System.InvalidOperationException($"{nameof(WindowsXamlHostBase)}::{nameof(WndProc)}: Failed to SetFocus on UWP XAML window");
                    }

                    base.WndProc(ref m);
                    break;

                case NativeDefines.WM_KILLFOCUS:
                    // If focus is being set on the UWP XAML island window then we should prevent LostFocus by
                    // handling this message.
                    if (_xamlIslandWindowHandle != IntPtr.Zero || _xamlIslandWindowHandle != m.WParam || _xamlSource.HasFocus)
                    {
                        base.WndProc(ref m);
                    }

                    break;

                case NativeDefines.WM_DPICHANGED_AFTERPARENT:
                    if (_xamlIslandWindowHandle != IntPtr.Zero)
                    {
                        UpdateDpiScalingFactor();
                        PerformLayout();
                    }

                    base.WndProc(ref m);
                    break;

                default:
                    base.WndProc(ref m);
                    break;
            }
        }
    }
}
