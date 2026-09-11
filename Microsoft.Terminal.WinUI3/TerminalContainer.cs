using Microsoft.Terminal.WinUI3.WPFImports;
using Microsoft.Terminal.Wpf;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Input;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Interop;
using Windows.ApplicationModel.DataTransfer;
using Windows.Win32;
using Windows.Win32.Foundation;
using WinUIEx.Messaging;

namespace Microsoft.Terminal.WinUI3 {


	/// <summary>
	/// The container class that hosts the native hwnd terminal.
	/// </summary>
	/// <remarks>
	/// This class is only left public since xaml cannot work with internal classes.
	/// </remarks>
	internal class TerminalContainer : HwndHost {
		private readonly TerminalConnectionLifecycle connectionLifecycle;
		private readonly TerminalRenderBuffer renderBuffer = new();
		private HWND hwnd;
		private IntPtr terminal;
		private NativeMethods.ScrollCallback scrollCallback;
		private NativeMethods.WriteCallback writeCallback;
		private readonly NativeMethods.SubclassProc selectionWindowSubclassProc;
		private bool selectionWindowSubclassInstalled;
		private bool suppressEnterUntilKeyUp;
		private bool terminalResizeInProgress;
		private bool nativeResizeQueued;
		private Size pendingTerminalRendererSize;
		private bool disposeStarted;
		private bool disposed;
		private bool nativeWindowDestroyed;

		// The native HwndTerminal starts a brand-new selection for every
		// WM_LBUTTONDOWN. This is normally correct, but it loses the selection
		// anchor after the user releases the mouse, moves the external WinUI
		// scrollbar and Shift-clicks a later line. Preserve the existing range by
		// feeding the native control the corresponding drag update instead of a
		// new button-down.
		private const int MouseLeftButtonMask = 0x0001;
		private const int MouseShiftMask = 0x0004;
		private static readonly UIntPtr SelectionWindowSubclassId = (UIntPtr)0x53465450;

		internal bool IsNativeReady => !this.IsNativeUnavailable;


		/// <summary>
		/// Initializes a new instance of the <see cref="TerminalContainer"/> class.
		/// </summary>
		public TerminalContainer() {

			this.connectionLifecycle = new(this.Connection_TerminalOutput);
			this.selectionWindowSubclassProc = this.SelectionWindowSubclassProc;
			this.MessageHook += this.TerminalContainer_MessageHook;
			this.GettingFocus += TerminalContainer_GettingFocus;
			this.IsTabStop = false;
		}
		internal void PassFocus() {
			if (this.IsNativeUnavailable || this.hwnd == HWND.Null) {
				return;
			}

			NativeMethods.SetFocus(this.hwnd);
		}
		private void TerminalContainer_GettingFocus(UIElement sender, GettingFocusEventArgs args) {
			if (this.IsNativeUnavailable) {
				return;
			}

			args.Handled = true;
			Debug.WriteLine($"TerminalContainer_GettingFocus setting to hwnd");
			PassFocus();
		}

		private bool IsNativeUnavailable =>
			this.disposeStarted ||
			this.disposed ||
			this.nativeWindowDestroyed ||
			Volatile.Read(ref this.terminal) == IntPtr.Zero;

		private void ResetNativeRendererState() {
			this.hwnd = HWND.Null;
			this.nativeResizeQueued = false;
			this.pendingTerminalRendererSize = Size.Empty;
			this.TerminalRendererSize = Size.Empty;
			this.Rows = 0;
			this.Columns = 0;
		}

		private void DestroyNativeTerminalOnce(IntPtr expectedTerminal) {
			if (expectedTerminal == IntPtr.Zero ||
				Interlocked.CompareExchange(ref this.terminal, IntPtr.Zero, expectedTerminal) != expectedTerminal) {
				return;
			}

			this.nativeWindowDestroyed = true;
			this.RemoveSelectionWindowSubclass();
			this.ResetNativeRendererState();
			try {
				NativeMethods.DestroyTerminal(expectedTerminal);
			} catch (Exception ex) {
				Debug.WriteLine($"Native terminal cleanup failed: {ex}");
			} finally {
				this.nativeWindowDestroyed = false;
			}
		}

		private static void DestroyDetachedNativeTerminal(IntPtr detachedTerminal) {
			if (detachedTerminal == IntPtr.Zero) {
				return;
			}

			try {
				NativeMethods.DestroyTerminal(detachedTerminal);
			} catch (Exception ex) {
				Debug.WriteLine($"Detached native terminal cleanup failed: {ex}");
			}
		}

		private void QueueNativeTerminalCleanup(IntPtr detachedTerminal) {
			if (detachedTerminal == IntPtr.Zero) {
				return;
			}

			try {
				if (!this.DispatcherQueue.TryEnqueue(() => DestroyDetachedNativeTerminal(detachedTerminal))) {
					Debug.WriteLine("Unable to queue detached native terminal cleanup because the dispatcher is shutting down.");
				}
			} catch (Exception ex) {
				Debug.WriteLine($"Unable to queue native terminal cleanup: {ex}");
			}
		}

		protected override void Dispose(bool disposing) {
			if (this.disposeStarted || this.disposed) {
				return;
			}

			this.disposeStarted = true;
			// The base finalizer path queues HWND destruction to the dispatcher. Detach
			// the native object first so that queued DestroyWindowCore and our deferred
			// cleanup cannot both destroy the same terminal.
			var finalizerDetachedTerminal = disposing
				? IntPtr.Zero
				: Interlocked.Exchange(ref this.terminal, IntPtr.Zero);
			try {
				this.nativeResizeQueued = false;
				if (disposing) {
					this.MessageHook -= this.TerminalContainer_MessageHook;
					this.GettingFocus -= this.TerminalContainer_GettingFocus;
					this.connectionLifecycle.SetConnection(null);
				}

				try {
					base.Dispose(disposing);
				} finally {
					var remainingTerminal = disposing
						? Volatile.Read(ref this.terminal)
						: finalizerDetachedTerminal;
					if (remainingTerminal != IntPtr.Zero) {
						if (disposing) {
							this.DestroyNativeTerminalOnce(remainingTerminal);
						} else {
							this.QueueNativeTerminalCleanup(remainingTerminal);
						}
					}
				}
			} finally {
				this.disposed = true;
				this.disposeStarted = false;
			}
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
				return this.connectionLifecycle.Current;
			}

			set {
				if ((this.disposeStarted || this.disposed) && value != null) {
					return;
				}

				this.connectionLifecycle.SetConnection(value);
			}
		}

		/// <summary>
		/// Manually invoke a scroll of the terminal buffer.
		/// </summary>
		/// <param name="viewTop">The top line to show in the terminal.</param>
		internal void UserScroll(int viewTop) {
			if (this.IsNativeUnavailable) {
				return;
			}

			NativeMethods.TerminalUserScroll(this.terminal, viewTop);
		}

		/// <summary>
		/// Sets the theme for the terminal. This includes font family, size, color, as well as background and foreground colors.
		/// </summary>
		/// <param name="theme">The color theme for the terminal to use.</param>
		/// <param name="fontFamily">The font family to use in the terminal.</param>
		/// <param name="fontSize">The font size to use in the terminal.</param>
		internal void SetTheme(TerminalTheme theme, string fontFamily, short fontSize) {
			if (this.IsNativeUnavailable) {
				return;
			}

			var dpiScale = curDPI;

			NativeMethods.TerminalSetTheme(this.terminal, theme, fontFamily, fontSize, (int)dpiScale.PixelsPerInchX);

			// Validate before resizing that we have a non-zero size.
			var resizeSize = !this.TerminalRendererSize.IsEmpty
				? this.TerminalRendererSize
				: this.TerminalControlSize;
			if (!this.RenderSize.IsEmpty && !resizeSize.IsEmpty
				&& resizeSize.Width != 0 && resizeSize.Height != 0) {
				this.Resize(resizeSize);
			}
		}

		/// <summary>
		/// Gets the selected text from the terminal renderer and clears the selection.
		/// </summary>
		/// <returns>The selected text, empty if no text is selected.</returns>
		internal string GetSelectedText() {
			if (this.IsNativeUnavailable) {
				return string.Empty;
			}

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
			if (this.IsNativeUnavailable) {
				return;
			}

			if (renderSize.Width == 0 || renderSize.Height == 0) {
				throw new ArgumentException("Terminal column or row count cannot be 0.", nameof(renderSize));
			}

			if (!this.TryTriggerNativeResize((int)renderSize.Width, (int)renderSize.Height, out NativeMethods.TilSize dimensions)) {
				return;
			}

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
			if (this.IsNativeUnavailable) {
				return;
			}

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

			if (this.terminal == IntPtr.Zero || this.terminalResizeInProgress) {
				return;
			}

			this.terminalResizeInProgress = true;
			try {
				NativeMethods.TerminalTriggerResizeWithDimension(this.terminal, dimensions, out var dimensionsInPixels);

				this.Columns = dimensions.X;
				this.Rows = dimensions.Y;

				this.TerminalRendererSize = new Size {
					Width = dimensionsInPixels.X,
					Height = dimensionsInPixels.Y,
				};

				this.Connection?.Resize((uint)dimensions.Y, (uint)dimensions.X);
			} finally {
				this.terminalResizeInProgress = false;
			}
		}

		private bool TryTriggerNativeResize(int width, int height, out NativeMethods.TilSize dimensions) {
			dimensions = default;
			if (this.IsNativeUnavailable || this.terminalResizeInProgress || width <= 0 || height <= 0) {
				return false;
			}

			this.terminalResizeInProgress = true;
			try {
				NativeMethods.TerminalTriggerResize(this.terminal, width, height, out dimensions);
				return true;
			} finally {
				this.terminalResizeInProgress = false;
			}
		}

		internal void QueueNativeResize(int width, int height) {
			if (this.IsNativeUnavailable || width <= 0 || height <= 0) {
				return;
			}

			if (!this.nativeResizeQueued &&
				this.TerminalRendererSize.Width == width &&
				this.TerminalRendererSize.Height == height) {
				return;
			}

			this.pendingTerminalRendererSize = new Size {
				Width = width,
				Height = height,
			};
			if (this.nativeResizeQueued) {
				return;
			}

			this.nativeResizeQueued = true;
			try {
				if (this.DispatcherQueue.TryEnqueue(this.ProcessQueuedNativeResize)) {
					return;
				}

				this.nativeResizeQueued = false;
			} catch (Exception ex) {
				Debug.WriteLine($"Unable to queue terminal resize: {ex}");
				this.nativeResizeQueued = false;
			}
		}

		private void ProcessQueuedNativeResize() {
			this.nativeResizeQueued = false;
			if (this.IsNativeUnavailable) {
				return;
			}

			try {
				var rendererSize = this.pendingTerminalRendererSize;
				if (rendererSize.Width <= 0 || rendererSize.Height <= 0 || this.TerminalRendererSize == rendererSize) {
					return;
				}

				if (!this.TryTriggerNativeResize((int)rendererSize.Width, (int)rendererSize.Height, out NativeMethods.TilSize dimensions) ||
					dimensions.X <= 0 || dimensions.Y <= 0) {
					return;
				}

				this.Columns = dimensions.X;
				this.Rows = dimensions.Y;
				this.TerminalRendererSize = rendererSize;
				this.Connection?.Resize((uint)dimensions.Y, (uint)dimensions.X);
			} catch (Exception ex) {
				Debug.WriteLine($"Terminal resize failed: {ex}");
			}
		}

		/// <summary>
		/// Calculates the rows and columns that would fit in the given size.
		/// </summary>
		/// <param name="size">DPI scaled size.</param>
		/// <returns>Amount of rows and columns that would fit the given size.</returns>
		internal (int columns, int rows) CalculateRowsAndColumns(Size size) {
			if (this.IsNativeUnavailable || size.Width <= 0 || size.Height <= 0) {
				return (0, 0);
			}

			NativeMethods.TerminalCalculateResize(this.terminal, (int)size.Width, (int)size.Height, out NativeMethods.TilSize dimensions);

			return (dimensions.X, dimensions.Y);
		}

		/// <summary>
		/// Triggers the terminal resize event if more space is available in the terminal control.
		/// </summary>
		internal void RaiseResizedIfDrawSpaceIncreased() {
			var (columns, rows) = this.CalculateRowsAndColumns(this.TerminalControlSize);

			if (columns > 0 && rows > 0 && (this.Columns < columns || this.Rows < rows)) {
				this.connectionLifecycle.Current?.Resize((uint)rows, (uint)columns);
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

		protected override void OnHostedWindowPositionApplied(int width, int height) {
			if (this.AutoResize && !this.IsNativeUnavailable) {
				// SetWindowPos uses SWP_NOCOPYBITS for the DirectX child HWND. Resize
				// the renderer only after that operation, otherwise a later HWND resize
				// discards the frame that TerminalTriggerResize has just drawn.
				this.QueueNativeResize(width, height);
			}
		}

		/// <inheritdoc/>
		protected override AutomationPeer OnCreateAutomationPeer() {
			return null;
		}

		/// <inheritdoc/>
		protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) {
			if (!this.IsNativeUnavailable) {
				NativeMethods.TerminalDpiChanged(this.terminal, (int)(NativeMethods.USER_DEFAULT_SCREEN_DPI * newDpi.DpiScaleX));

				var pixelWidth = (int)Math.Clamp(
					Math.Round(this.RenderSize.Width * newDpi.DpiScaleX),
					0,
					int.MaxValue);
				var pixelHeight = (int)Math.Clamp(
					Math.Round(this.RenderSize.Height * newDpi.DpiScaleY),
					0,
					int.MaxValue);
				this.TerminalControlSize = new Size(pixelWidth, pixelHeight);
				if (this.AutoResize) {
					// The pixel rectangle can stay unchanged while the DPI-dependent cell
					// metrics change. Force the renderer to recalculate rows and columns.
					this.TerminalRendererSize = Size.Empty;
					this.QueueNativeResize(pixelWidth, pixelHeight);
				}
			}

			base.OnDpiChanged(oldDpi, newDpi);
		}

		/// <inheritdoc/>
		protected override HWND BuildWindowCore(HWND hwndParent) {
			var previousTerminal = Volatile.Read(ref this.terminal);
			if (previousTerminal != IntPtr.Zero) {
				this.DestroyNativeTerminalOnce(previousTerminal);
			}

			this.nativeWindowDestroyed = false;
			var dpiScale = curDPI;
			var createdTerminal = IntPtr.Zero;
			var createdHwnd = HWND.Null;
			var published = false;
			try {
				NativeMethods.CreateTerminal(hwndParent, out var hostedHwnd, out createdTerminal);
				createdHwnd = new HWND(hostedHwnd);
				if (createdTerminal == IntPtr.Zero || createdHwnd == HWND.Null || !PInvoke.IsWindow(createdHwnd)) {
					throw new InvalidOperationException("Native terminal window was not created.");
				}

				this.scrollCallback = this.OnScroll;
				this.writeCallback = this.OnWrite;
				NativeMethods.TerminalRegisterScrollCallback(createdTerminal, this.scrollCallback);
				NativeMethods.TerminalRegisterWriteCallback(createdTerminal, this.writeCallback);

				// If the saved DPI scale isn't the default scale, we push it to the terminal.
				if (dpiScale.PixelsPerInchX != NativeMethods.USER_DEFAULT_SCREEN_DPI) {
					NativeMethods.TerminalDpiChanged(createdTerminal, (int)dpiScale.PixelsPerInchX);
				}

				this.hwnd = createdHwnd;
				Interlocked.Exchange(ref this.terminal, createdTerminal);
				published = true;
				this.InstallSelectionWindowSubclass();

				// The native control has no drawable viewport until its first resize.
				// Establish rows/columns before replaying saved output; otherwise the
				// first prompt/history remains invisible until a later key event causes
				// the renderer to repaint.
				var initialWidth = this.TerminalControlSize.Width > 0
					? this.TerminalControlSize.Width
					: (int)Math.Round(this.RenderSize.Width * dpiScale.DpiScaleX);
				var initialHeight = this.TerminalControlSize.Height > 0
					? this.TerminalControlSize.Height
					: (int)Math.Round(this.RenderSize.Height * dpiScale.DpiScaleY);
				if (this.AutoResize &&
					initialWidth > 0 &&
					initialHeight > 0 &&
					this.TryTriggerNativeResize(
						initialWidth,
						initialHeight,
						out var initialDimensions)) {
					this.Columns = initialDimensions.X;
					this.Rows = initialDimensions.Y;
					this.TerminalRendererSize = new Size(initialWidth, initialHeight);
				}

				// Apply defaults only after the native viewport exists. Do not notify
				// application code from inside BuildWindowCore: binding a backend here
				// can synchronously feed output back into native window construction.
				NativeMethods.TerminalSetTheme(
					createdTerminal,
					TerminalThemeDefaults.Create(),
					TerminalThemeDefaults.FontFamily,
					TerminalThemeDefaults.FontSize,
					(int)dpiScale.PixelsPerInchX);
				ReplayTerminalBuffer();
				NativeMethods.RequestTerminalRepaint(createdHwnd);

				if (this.AutoResize && this.TerminalControlSize.Width > 0 && this.TerminalControlSize.Height > 0) {
					this.QueueNativeResize(this.TerminalControlSize.Width, this.TerminalControlSize.Height);
				}

				return this.hwnd;
			} catch {
				if (published) {
					this.DestroyNativeTerminalOnce(createdTerminal);
				} else {
					this.ResetNativeRendererState();
					if (createdTerminal != IntPtr.Zero) {
						try {
							NativeMethods.DestroyTerminal(createdTerminal);
						} catch (Exception cleanupException) {
							Debug.WriteLine($"Native terminal build rollback failed: {cleanupException}");
						}
					} else if (createdHwnd != HWND.Null && PInvoke.IsWindow(createdHwnd)) {
						_ = PInvoke.DestroyWindow(createdHwnd);
					}
				}

				throw;
			}
		}

		/// <inheritdoc/>
		protected override void DestroyWindowCore(HWND hwnd) {
			var currentTerminal = Volatile.Read(ref this.terminal);
			if (currentTerminal != IntPtr.Zero) {
				this.DestroyNativeTerminalOnce(currentTerminal);
			}

			if (hwnd != HWND.Null && PInvoke.IsWindow(hwnd)) {
				_ = PInvoke.DestroyWindow(hwnd);
			}

			this.ResetNativeRendererState();
		}

		/// <inheritdoc/>
		protected override void OnHostedWindowDestroyed(HWND hwnd) {
			this.nativeWindowDestroyed = true;
			this.ResetNativeRendererState();
			// Detach ownership before deferring destruction. A replacement can now be
			// published safely: it cannot be confused with this pointer even if a later
			// allocation eventually reuses the same address.
			var detachedTerminal = Interlocked.Exchange(ref this.terminal, IntPtr.Zero);
			if (detachedTerminal == IntPtr.Zero) {
				this.nativeWindowDestroyed = false;
				return;
			}

			// WM_NCDESTROY is still on the native window stack. Defer destroying the
			// detached terminal object until the message unwinds.
			this.QueueNativeTerminalCleanup(detachedTerminal);
			this.nativeWindowDestroyed = false;
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

		private static bool IsShiftSelectionExtensionGesture(IntPtr wParam) {
			// WM_LBUTTONDOWN carries the physical modifier state. Do not consult the
			// WinUI keyboard state here: after an external scrollbar or a wheel
			// message, its state can be observed on a different input path.
			return ((long)wParam & MouseShiftMask) != 0;
		}

		private void InstallSelectionWindowSubclass() {
			if (this.selectionWindowSubclassInstalled || this.hwnd == HWND.Null) {
				return;
			}

			this.selectionWindowSubclassInstalled = NativeMethods.SetWindowSubclass(
				this.hwnd,
				this.selectionWindowSubclassProc,
				SelectionWindowSubclassId,
				IntPtr.Zero);
			if (!this.selectionWindowSubclassInstalled) {
				Debug.WriteLine("Unable to install the terminal Shift-selection subclass.");
			}
		}

		private void RemoveSelectionWindowSubclass() {
			if (!this.selectionWindowSubclassInstalled) {
				return;
			}

			_ = NativeMethods.RemoveWindowSubclass(this.hwnd, this.selectionWindowSubclassProc, SelectionWindowSubclassId);
			this.selectionWindowSubclassInstalled = false;
		}

		private IntPtr SelectionWindowSubclassProc(
			IntPtr hwnd,
			uint message,
			IntPtr wParam,
			IntPtr lParam,
			UIntPtr subclassId,
			IntPtr referenceData) {
			if (message == (uint)WindowsMessages.LBUTTONDOWN &&
				!this.IsNativeUnavailable &&
				IsShiftSelectionExtensionGesture(wParam) &&
				NativeMethods.TerminalIsSelectionActive(this.terminal)) {
				// HwndTerminal clears selection for every WM_LBUTTONDOWN. Intercept at
				// the native HWND boundary (not MessageHook) so scrolling cannot make a
				// later Shift-click replace the original selection anchor.
				ExtendSelectionFromShiftClick(lParam);
				return IntPtr.Zero;
			}

			return NativeMethods.DefSubclassProc(hwnd, message, wParam, lParam);
		}

		private void ExtendSelectionFromShiftClick(IntPtr lParam) {
			// HwndTerminal extends a selection from WM_MOUSEMOVE while the left button
			// is down. Sending that event preserves its original buffer-coordinate
			// anchor even when the viewport was moved by TerminalUserScroll.
			NativeMethods.SendMessage(
				this.hwnd,
				(uint)WindowsMessages.MOUSEMOVE,
				(IntPtr)MouseLeftButtonMask,
				lParam);
		}


		private void TerminalContainer_MessageHook(object sender, WindowMessageEventArgs e) {
			if (this.IsNativeUnavailable) {
				return;
			}

			var msg = e.Message;
			var hwnd = msg.Hwnd;
			var wParam = (nint)msg.WParam;
			var lParam = msg.LParam;
			if (hwnd == this.hwnd) {
				switch ((WindowsMessages)e.Message.MessageId) {
					case WindowsMessages.SETFOCUS:
						NativeMethods.TerminalSetFocused(this.terminal, true);
						break;
					case WindowsMessages.KILLFOCUS:
						suppressEnterUntilKeyUp = false;
						NativeMethods.TerminalSetFocused(this.terminal, false);
						break;
					case WindowsMessages.MOUSEACTIVATE:
						this.Focus(FocusState.Pointer);
						NativeMethods.SetFocus(this.hwnd);
						break;

					case WindowsMessages.SYSKEYDOWN: // fallthrough
					case WindowsMessages.KeyDown: {
							UnpackKeyMessage(wParam, lParam, out ushort vkey, out ushort scanCode, out ushort flags);
							if (vkey == 0x0D && (suppressEnterUntilKeyUp || CopySelectionToClipboard())) {
								suppressEnterUntilKeyUp = true;
								e.Handled = true;
								break;
							}
							NativeMethods.TerminalSendKeyEvent(this.terminal, vkey, scanCode, flags, true);
							break;
						}

					case WindowsMessages.SYSKEYUP: // fallthrough
					case WindowsMessages.KeyUp: {
							// WM_KEYUP lParam layout documentation: https://docs.microsoft.com/en-us/windows/win32/inputdev/wm-keyup
							UnpackKeyMessage(wParam, lParam, out ushort vkey, out ushort scanCode, out ushort flags);
							if (vkey == 0x0D && suppressEnterUntilKeyUp) {
								suppressEnterUntilKeyUp = false;
								e.Handled = true;
								break;
							}
							NativeMethods.TerminalSendKeyEvent(this.terminal, (ushort)wParam, scanCode, flags, false);
							break;
						}

					case WindowsMessages.Char: {
							// WM_CHAR lParam layout documentation: https://docs.microsoft.com/en-us/windows/win32/inputdev/wm-char
							UnpackCharMessage(wParam, lParam, out char character, out ushort scanCode, out ushort flags);
							if (character == '\r' && suppressEnterUntilKeyUp) {
								e.Handled = true;
								break;
							}
							NativeMethods.TerminalSendCharEvent(this.terminal, character, scanCode, flags);
							break;
						}

					case WindowsMessages.RBUTTONDOWN:
						e.Handled = true;
						break;

					case WindowsMessages.MouseRightButtonUp:
						e.Handled = true;
						_ = PasteClipboardAsync();
						break;

					case WindowsMessages.MOUSEWHEEL:
						var delta = (short)(((long)wParam) >> 16);
						this.UserScrolled?.Invoke(this, delta);
						// Do not let the native terminal process the wheel as a mouse-reporting
						// event. When a remote application enables SGR mouse mode, it would
						// otherwise receive ESC[<64;...M sequences instead of a local
						// scrollback request.
						e.Handled = true;
						break;
				}
			}

			//e.re IntPtr.Zero;
		}

		private bool CopySelectionToClipboard() {
			if (this.IsNativeUnavailable || !NativeMethods.TerminalIsSelectionActive(this.terminal)) {
				return false;
			}

			var selectedText = NativeMethods.TerminalGetSelection(this.terminal);
			if (string.IsNullOrEmpty(selectedText)) {
				return true;
			}

			try {
				var dataPackage = new DataPackage {
					RequestedOperation = DataPackageOperation.Copy,
				};
				dataPackage.SetText(selectedText);
				Clipboard.SetContent(dataPackage);
				Clipboard.Flush();
				return true;
			} catch (Exception ex) {
				Debug.WriteLine($"Terminal clipboard copy failed: {ex.Message}");
				// The Enter key still belongs to the selection gesture. Do not let a
				// clipboard failure accidentally execute the current shell line.
				return true;
			}
		}

		private async System.Threading.Tasks.Task PasteClipboardAsync() {
			try {
				var content = Clipboard.GetContent();
				if (!content.Contains(StandardDataFormats.Text)) {
					return;
				}

				var text = await content.GetTextAsync();
				if (string.IsNullOrEmpty(text)) {
					return;
				}

				var terminalText = text
					.Replace("\r\n", "\n", StringComparison.Ordinal)
					.Replace('\r', '\n')
					.Replace("\n", "\r", StringComparison.Ordinal);
				this.Connection?.WriteInput(terminalText);
			} catch (Exception ex) {
				Debug.WriteLine($"Terminal clipboard paste failed: {ex.Message}");
			}
		}

		private void Connection_TerminalOutput(object sender, TerminalOutputEventArgs e) {
			if (string.IsNullOrEmpty(e.Data)) {
				return;
			}

			renderBuffer.Append(e.Data);
			if (this.IsNativeUnavailable) {
				return;
			}

			NativeMethods.TerminalSendOutput(this.terminal, e.Data);
			NativeMethods.RequestTerminalRepaint(this.hwnd);
		}

		private void ReplayTerminalBuffer() {
			if (this.IsNativeUnavailable) {
				return;
			}

			var output = renderBuffer.Snapshot();
			if (!string.IsNullOrEmpty(output)) {
				NativeMethods.TerminalSendOutput(this.terminal, output);
				NativeMethods.RequestTerminalRepaint(this.hwnd);
			}
		}

		private void OnScroll(int viewTop, int viewHeight, int bufferSize) {
			if (this.IsNativeUnavailable) {
				return;
			}

			this.TerminalScrolled?.Invoke(this, (viewTop, viewHeight, bufferSize));
		}

		private void OnWrite(string data) {
			if (this.IsNativeUnavailable) {
				return;
			}

			this.Connection?.WriteInput(data);
		}
	}
}
