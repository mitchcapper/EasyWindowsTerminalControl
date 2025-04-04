using Windows.Win32.Foundation;

using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinUIEx.Messaging;
using Microsoft.UI.Xaml.Input;
using System.Windows.Interop;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.Terminal.WinUI3.WPFImports;
using Microsoft.Terminal.Wpf;

namespace Microsoft.Terminal.WinUI3 {


	/// <summary>
	/// The container class that hosts the native hwnd terminal.
	/// </summary>
	/// <remarks>
	/// This class is only left public since xaml cannot work with internal classes.
	/// </remarks>
	internal class TerminalContainer : HwndHost {
		private ITerminalConnection connection;
		private HWND hwnd;
		private IntPtr terminal;
		private DispatcherTimer blinkTimer;
		private NativeMethods.ScrollCallback scrollCallback;
		private NativeMethods.WriteCallback writeCallback;

		/// <summary>
		/// Initializes a new instance of the <see cref="TerminalContainer"/> class.
		/// </summary>
		public TerminalContainer() {

			this.MessageHook += this.TerminalContainer_MessageHook;
			this.GettingFocus += TerminalContainer_GettingFocus;
			this.IsTabStop=false;

			var blinkTime = NativeMethods.GetCaretBlinkTime();

			if (blinkTime == uint.MaxValue) {
				return;
			}

			this.blinkTimer = new DispatcherTimer();
			this.blinkTimer.Interval = TimeSpan.FromMilliseconds(blinkTime);
			this.blinkTimer.Tick += (_, __) => {
				if (this.terminal != IntPtr.Zero) {
					NativeMethods.TerminalBlinkCursor(this.terminal);
				}
			};
		}
		internal void PassFocus(){
			NativeMethods.SetFocus(this.hwnd);
		}
		private void TerminalContainer_GettingFocus(UIElement sender, GettingFocusEventArgs args) {
			args.Handled=true;
			Debug.WriteLine($"TerminalContainer_GettingFocus setting to hwnd");
			PassFocus();
		}

		/// <summary>
		/// Event that is fired when the terminal buffer scrolls from text output.
		/// </summary>
		internal event EventHandler<(int viewTop, int viewHeight, int bufferSize)> TerminalScrolled;

		/// <summary>
		/// Event that is fired when the user engages in a mouse scroll over the terminal hwnd.
		/// </summary>
		internal event EventHandler<int> UserScrolled;

		/// <summary>
		/// Gets or sets a value indicating whether if the renderer should automatically resize to fill the control
		/// on user action.
		/// </summary>
		internal bool AutoResize { get; set; } = true;

		/// <summary>
		/// Gets or sets the size of the parent user control that hosts the terminal hwnd.
		/// </summary>
		/// <remarks>Control size is in device independent units, but for simplicity all sizes should be scaled.</remarks>
		internal Size TerminalControlSize { get; set; }

		/// <summary>
		/// Gets or sets the size of the terminal renderer.
		/// </summary>
		internal Size TerminalRendererSize { get; set; }

		/// <summary>
		/// Gets the current character rows available to the terminal.
		/// </summary>
		internal int Rows { get; private set; }

		/// <summary>
		/// Gets the current character columns available to the terminal.
		/// </summary>
		internal int Columns { get; private set; }

		/// <summary>
		/// Gets the window handle of the terminal.
		/// </summary>
		internal IntPtr Hwnd => this.hwnd;

		/// <summary>
		/// Sets the connection to the terminal backend.
		/// </summary>
		internal ITerminalConnection Connection {
			private get {
				return this.connection;
			}

			set {
				if (this.connection != null) {
					this.connection.TerminalOutput -= this.Connection_TerminalOutput;
				}

				this.Connection_TerminalOutput(this, new TerminalOutputEventArgs("\x001bc\x1b]104\x1b\\")); // reset console/clear screen - https://github.com/microsoft/terminal/pull/15062#issuecomment-1505654110
				var wasNull = this.connection == null;
				this.connection = value;
				if (this.connection != null) {
					if (wasNull) {
						this.Connection_TerminalOutput(this, new TerminalOutputEventArgs("\x1b[?25h")); // show cursor
					}

					this.connection.TerminalOutput += this.Connection_TerminalOutput;
					this.connection.Start();
				} else {
					this.Connection_TerminalOutput(this, new TerminalOutputEventArgs("\x1b[?25l")); // hide cursor
				}
			}
		}

		/// <summary>
		/// Manually invoke a scroll of the terminal buffer.
		/// </summary>
		/// <param name="viewTop">The top line to show in the terminal.</param>
		internal void UserScroll(int viewTop) {
			NativeMethods.TerminalUserScroll(this.terminal, viewTop);
		}

		/// <summary>
		/// Sets the theme for the terminal. This includes font family, size, color, as well as background and foreground colors.
		/// </summary>
		/// <param name="theme">The color theme for the terminal to use.</param>
		/// <param name="fontFamily">The font family to use in the terminal.</param>
		/// <param name="fontSize">The font size to use in the terminal.</param>
		internal void SetTheme(TerminalTheme theme, string fontFamily, short fontSize) {
			var dpiScale = curDPI;

			NativeMethods.TerminalSetTheme(this.terminal, theme, fontFamily, fontSize, (int)dpiScale.PixelsPerInchX);

			// Validate before resizing that we have a non-zero size.
			if (!this.RenderSize.IsEmpty && !this.TerminalControlSize.IsEmpty
				&& this.TerminalControlSize.Width != 0 && this.TerminalControlSize.Height != 0) {
				this.Resize(this.TerminalControlSize);
			}
		}

		/// <summary>
		/// Gets the selected text from the terminal renderer and clears the selection.
		/// </summary>
		/// <returns>The selected text, empty if no text is selected.</returns>
		internal string GetSelectedText() {
			if (NativeMethods.TerminalIsSelectionActive(this.terminal)) {
				return NativeMethods.TerminalGetSelection(this.terminal);
			}

			return string.Empty;
		}

		/// <summary>
		/// Triggers a resize of the terminal with the given size, redrawing the rendered text.
		/// </summary>
		/// <param name="renderSize">Size of the rendering window.</param>
		internal void Resize(Size renderSize) {
			if (renderSize.Width == 0 || renderSize.Height == 0) {
				throw new ArgumentException("Terminal column or row count cannot be 0.", nameof(renderSize));
			}

			NativeMethods.TerminalTriggerResize(
				this.terminal,
				(int)renderSize.Width,
				(int)renderSize.Height,
				out NativeMethods.TilSize dimensions);

			this.Rows = dimensions.Y;
			this.Columns = dimensions.X;
			this.TerminalRendererSize = renderSize;

			this.Connection?.Resize((uint)dimensions.Y, (uint)dimensions.X);
		}

		/// <summary>
		/// Resizes the terminal using row and column count as the new size.
		/// </summary>
		/// <param name="rows">Number of rows to show.</param>
		/// <param name="columns">Number of columns to show.</param>
		internal void Resize(uint rows, uint columns) {
			if (rows == 0) {
				throw new ArgumentException("Terminal row count cannot be 0.", nameof(rows));
			}

			if (columns == 0) {
				throw new ArgumentException("Terminal column count cannot be 0.", nameof(columns));
			}

			NativeMethods.TilSize dimensions = new NativeMethods.TilSize {
				X = (int)columns,
				Y = (int)rows,
			};

			NativeMethods.TerminalTriggerResizeWithDimension(this.terminal, dimensions, out var dimensionsInPixels);

			this.Columns = dimensions.X;
			this.Rows = dimensions.Y;

			this.TerminalRendererSize = new Size {
				Width = dimensionsInPixels.X,
				Height = dimensionsInPixels.Y,
			};

			this.Connection?.Resize((uint)dimensions.Y, (uint)dimensions.X);
		}

		/// <summary>
		/// Calculates the rows and columns that would fit in the given size.
		/// </summary>
		/// <param name="size">DPI scaled size.</param>
		/// <returns>Amount of rows and columns that would fit the given size.</returns>
		internal (int columns, int rows) CalculateRowsAndColumns(Size size) {
			NativeMethods.TerminalCalculateResize(this.terminal, (int)size.Width, (int)size.Height, out NativeMethods.TilSize dimensions);

			return (dimensions.X, dimensions.Y);
		}

		/// <summary>
		/// Triggers the terminal resize event if more space is available in the terminal control.
		/// </summary>
		internal void RaiseResizedIfDrawSpaceIncreased() {
			var (columns, rows) = this.CalculateRowsAndColumns(this.TerminalControlSize);

			if (this.Columns < columns || this.Rows < rows) {
				this.connection?.Resize((uint)rows, (uint)columns);
			}
		}

		/// <summary>
		/// WPF's HwndHost likes to mark the WM_GETOBJECT message as handled to
		/// force the usage of the WPF automation peer. We explicitly mark it as
		/// not handled and don't return an automation peer in "OnCreateAutomationPeer" below.
		/// This forces the message to go down to the HwndTerminal where we return terminal's UiaProvider.
		/// </summary>
		/// <inheritdoc/>
		protected override void WndProc(WindowMessageEventArgs e) {
			if ((WindowsMessages)e.Message.MessageId == WindowsMessages.GETOBJECT) {
				e.Handled = false;
				return;
			}

			base.WndProc(e);
		}

		/// <inheritdoc/>
		protected override AutomationPeer OnCreateAutomationPeer() {
			return null;
		}

		/// <inheritdoc/>
		protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) {
			if (this.terminal != IntPtr.Zero) {
				NativeMethods.TerminalDpiChanged(this.terminal, (int)(NativeMethods.USER_DEFAULT_SCREEN_DPI * newDpi.DpiScaleX));
			}
		}

		/// <inheritdoc/>
		protected override HWND BuildWindowCore(HWND hwndParent) {
			var dpiScale = curDPI;
			NativeMethods.CreateTerminal(hwndParent, out var hostedHwnd, out this.terminal);
			this.hwnd =new( hostedHwnd);

			this.scrollCallback = this.OnScroll;
			this.writeCallback = this.OnWrite;

			NativeMethods.TerminalRegisterScrollCallback(this.terminal, this.scrollCallback);
			NativeMethods.TerminalRegisterWriteCallback(this.terminal, this.writeCallback);

			// If the saved DPI scale isn't the default scale, we push it to the terminal.
			if (dpiScale.PixelsPerInchX != NativeMethods.USER_DEFAULT_SCREEN_DPI) {
				NativeMethods.TerminalDpiChanged(this.terminal, (int)dpiScale.PixelsPerInchX);
			}

			if (NativeMethods.GetFocus() == this.hwnd) {
				this.blinkTimer?.Start();
			} else {
				NativeMethods.TerminalSetCursorVisible(this.terminal, false);
			}

			return this.hwnd;
		}

		/// <inheritdoc/>
		protected override void DestroyWindowCore(HWND hwnd) {
			NativeMethods.DestroyTerminal(this.terminal);
			this.terminal = IntPtr.Zero;
		}

		private static void UnpackKeyMessage(IntPtr wParam, IntPtr lParam, out ushort vkey, out ushort scanCode, out ushort flags) {
			ulong scanCodeAndFlags = ((ulong)lParam >> 16) & 0xFFFF;
			scanCode = (ushort)(scanCodeAndFlags & 0x00FFu);
			flags = (ushort)(scanCodeAndFlags & 0xFF00u);
			vkey = (ushort)wParam;
		}

		private static void UnpackCharMessage(IntPtr wParam, IntPtr lParam, out char character, out ushort scanCode, out ushort flags) {
			UnpackKeyMessage(wParam, lParam, out ushort vKey, out scanCode, out flags);
			character = (char)vKey;
		}


		private void TerminalContainer_MessageHook(object sender, WindowMessageEventArgs e) {
			var msg = e.Message;
			var hwnd = msg.Hwnd;
			var wParam = (nint)msg.WParam;
			var lParam = msg.LParam;
			if (hwnd == this.hwnd) {
				switch ((WindowsMessages)e.Message.MessageId) {
					case WindowsMessages.SETFOCUS:
						NativeMethods.TerminalSetFocus(this.terminal);
						this.blinkTimer?.Start();
						break;
					case WindowsMessages.KILLFOCUS:
						NativeMethods.TerminalKillFocus(this.terminal);
						this.blinkTimer?.Stop();
						NativeMethods.TerminalSetCursorVisible(this.terminal, false);
						break;
					case WindowsMessages.MOUSEACTIVATE:
						this.Focus(FocusState.Pointer);
						NativeMethods.SetFocus(this.hwnd);
						break;
					case WindowsMessages.SYSKEYDOWN: // fallthrough
					case WindowsMessages.KeyDown: {
							// WM_KEYDOWN lParam layout documentation: https://docs.microsoft.com/en-us/windows/win32/inputdev/wm-keydown
							NativeMethods.TerminalSetCursorVisible(this.terminal, true);

							UnpackKeyMessage(wParam, lParam, out ushort vkey, out ushort scanCode, out ushort flags);
							NativeMethods.TerminalSendKeyEvent(this.terminal, vkey, scanCode, flags, true);
							this.blinkTimer?.Start();
							break;
						}

					case WindowsMessages.SYSKEYUP: // fallthrough
					case WindowsMessages.KeyUp: {
							// WM_KEYUP lParam layout documentation: https://docs.microsoft.com/en-us/windows/win32/inputdev/wm-keyup
							UnpackKeyMessage(wParam, lParam, out ushort vkey, out ushort scanCode, out ushort flags);
							NativeMethods.TerminalSendKeyEvent(this.terminal, (ushort)wParam, scanCode, flags, false);
							break;
						}

					case WindowsMessages.Char: {
							// WM_CHAR lParam layout documentation: https://docs.microsoft.com/en-us/windows/win32/inputdev/wm-char
							UnpackCharMessage(wParam, lParam, out char character, out ushort scanCode, out ushort flags);
							NativeMethods.TerminalSendCharEvent(this.terminal, character, scanCode, flags);
							break;
						}

					case WindowsMessages.WINDOWPOSCHANGED:
						var windowpos = (NativeMethods.WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(NativeMethods.WINDOWPOS));
						if (((NativeMethods.SetWindowPosFlags)windowpos.flags).HasFlag(NativeMethods.SetWindowPosFlags.SWP_NOSIZE)
							|| (windowpos.cx == 0 && windowpos.cy == 0)) {
							break;
						}

						NativeMethods.TilSize dimensions;

						if (this.AutoResize) {
							NativeMethods.TerminalTriggerResize(this.terminal, windowpos.cx, windowpos.cy, out dimensions);

							this.Columns = dimensions.X;
							this.Rows = dimensions.Y;

							this.TerminalRendererSize = new Size {
								Width = windowpos.cx,
								Height = windowpos.cy,
							};
						} else {
							// Calculate the new columns and rows that fit the total control size and alert the control to redraw the margins.
							NativeMethods.TerminalCalculateResize(this.terminal, (int)this.TerminalControlSize.Width, (int)this.TerminalControlSize.Height, out dimensions);
						}

						this.Connection?.Resize((uint)dimensions.Y, (uint)dimensions.X);
						break;

					case WindowsMessages.MOUSEWHEEL:
						var delta = (short)(((long)wParam) >> 16);
						this.UserScrolled?.Invoke(this, delta);
						break;
				}
			}

			//e.re IntPtr.Zero;
		}

		private void Connection_TerminalOutput(object sender, TerminalOutputEventArgs e) {
			if (this.terminal == IntPtr.Zero || string.IsNullOrEmpty(e.Data)) {
				return;
			}

			NativeMethods.TerminalSendOutput(this.terminal, e.Data);
		}

		private void OnScroll(int viewTop, int viewHeight, int bufferSize) {
			this.TerminalScrolled?.Invoke(this, (viewTop, viewHeight, bufferSize));
		}

		private void OnWrite(string data) {
			this.Connection?.WriteInput(data);
		}
	}
}
