using Microsoft.Terminal.WinUI3;
using Microsoft.Terminal.WinUI3.WPFImports;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Diagnostics;
using System.Numerics;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using WinUIEx.Messaging;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;



// Disable pragma warnings to enable PREsharp pragmas
#pragma warning disable 1634, 1691

namespace System.Windows.Interop {
	/// <summary>
	/// A mostly complete hackish reimplementation of the WPF HwndHost, draw code specifically removed as Terminal did not need but can be ported
	/// </summary>
	internal abstract class HwndHost : FrameworkElement, IDisposable, IKeyboardInputSink {
		[System.Runtime.InteropServices.DllImport("gdi32.dll", SetLastError = true)]
		private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

		[System.Runtime.InteropServices.DllImport("gdi32.dll", SetLastError = true)]
		[return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
		private static extern bool DeleteObject(IntPtr hObject);

		[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
		private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn,
			[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] bool redraw);

		private double? _leftOverlayBoundaryInXamlRootDips;
		private bool _hasAppliedWindowRegion;
		private int _lastRegionLeft = -1;
		private int _lastRegionWidth = -1;
		private int _lastRegionHeight = -1;

		static HwndHost() {

		}

		/// <summary>
		///     Constructs an instance of the HwndHost class.
		/// </summary>
		///<remarks> Not available in Internet zone</remarks>
		protected HwndHost() {
			Initialize(false);
		}

		internal HwndHost(bool fTrusted) {
			Initialize(fTrusted);

		}
		protected GeneralTransform GetTransformToXamlRootContent() => this.TransformToVisual(this.XamlRoot.Content);

		/// <summary>
		///    Because we own an HWND, we implement a finalizer to make sure that we destroy it.
		/// </summary>
		~HwndHost() {
			Dispose(false);
		}

		/// <summary>
		///     Disposes this object.
		/// </summary>
		public void Dispose() {
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		/// <summary>
		///     The Win32 handle of the hosted window.
		/// </summary>
		public IntPtr Handle {
			get {
				if (_hwnd != HWND.Null) {
					if (!PInvoke.IsWindow(_hwnd)) {
						_hwnd = HWND.Null;
	}
}
				return _hwnd;
			}
		}

		internal void SetLeftOverlayBoundary(double? boundaryInXamlRootDips) {
			if (_leftOverlayBoundaryInXamlRootDips == boundaryInXamlRootDips) {
				return;
			}

			_leftOverlayBoundaryInXamlRootDips = boundaryInXamlRootDips;
			_lastRegionLeft = -1;
			_lastRegionWidth = -1;
			_lastRegionHeight = -1;

			if (!_hwnd.IsNull) {
				QueueWindowPositionUpdate();
			}
		}
		protected HWND XAMLParentWindow {
			get {
				if (_XAMLParentWindow.IsNull)
					UpdateXAMLRootHwnd();
				return _XAMLParentWindow;
			}
		}

		private void UpdateXAMLRootHwnd() {
			var rootHwnd = HWND.Null;
			var appId = this.XamlRoot?.ContentIslandEnvironment?.AppWindowId;
			if (appId != null)
				rootHwnd = new HWND(Win32Interop.GetWindowFromWindowId(appId.Value));
			if (rootHwnd.IsNull && _XAMLParentWindow.IsNull)
				return;

			lock (this) {
				if (rootHwnd == _XAMLParentWindow)
					return;
				if (!_XAMLParentWindow.IsNull) {
					xamlParentMessageMonitor.WindowMessageReceived -= xamlParentWndProc;
					xamlParentMessageMonitor.Dispose();
					xamlParentMessageMonitor = null;

				}
				_XAMLParentWindow = rootHwnd;
				if (rootHwnd != HWND.Null) {


					var inputWindow = UWPHelpers.GetInputHwnd(rootHwnd);
					xamlParentMessageMonitor = new WindowMessageMonitor(inputWindow);
					xamlParentMessageMonitor.WindowMessageReceived += xamlParentWndProc;
				}
			}
			OnSourceChanged(this);
		}

		private HWND _XAMLParentWindow;

		private void UpdateCurDPI() {
			var newScale = PInvoke.GetDpiForWindow(XAMLParentWindow) / 96f;
			curDPI = new DpiScale(newScale, newScale);
		}
		public DpiScale curDPI {
			get {
				if (_curDPI.DpiScaleX == 0)
					UpdateCurDPI();
				return _curDPI;
			}
			protected set {
				_curDPI = value;
			}
		}
		private DpiScale _curDPI;
		private void xamlParentWndProc(object sender, WindowMessageEventArgs e) {
			switch ((WindowsMessages)e.Message.MessageId) {
				case WindowsMessages.DPICHANGED:
					var oldDPI = curDPI;
					UpdateCurDPI();
					OnDpiChanged(oldDPI, curDPI); ;
					break;
				case WindowsMessages.KeyDown:
				case WindowsMessages.KeyUp:
				case WindowsMessages.SYSKEYDOWN:
				case WindowsMessages.IME_KEYDOWN:
				case WindowsMessages.SYSKEYUP:
				case WindowsMessages.IME_KEYUP:
					LastKeyboardMessage = e;

					break;
			}
		}
		private WindowMessageEventArgs LastKeyboardMessage;

		protected WindowMessageMonitor xamlParentMessageMonitor;
		protected WindowMessageMonitor _hwndSubclassHook;
		private long? _visibilityChangedToken;
		/// <summary>
		///     An event that is notified of all unhandled messages received
		///     by the hosted window.
		/// </summary>
		public event EventHandler<WindowMessageEventArgs> MessageHook;

		/// <summary>
		///     This event is raised after the DPI of the screen on which the HwndHost is displayed, changes.
		/// </summary>
		public event DpiChangedEventHandler DpiChanged {
			add { AddHandler(HwndHost.DpiChangedEvent, value, false); }
			remove { RemoveHandler(HwndHost.DpiChangedEvent, value); }
		}

		/// <summary>
		/// RoutedEvent for when DPI of the screen the HwndHost is on, changes.
		/// </summary>
		public static readonly RoutedEvent DpiChangedEvent;





		/// <summary>
		/// OnDpiChanged is called when the DPI at which this HwndHost is rendered, changes.
		/// </summary>
		protected virtual void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) {
			//RaiseEvent(new DpiChangedEventArgs(oldDpi, newDpi, HwndHost.DpiChangedEvent, this));
			InvalidateWindowPositionCache();
			QueueWindowPositionUpdate();
		}


		private static bool IsKeyPressed(VirtualKey key) {
			return (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key) == CoreVirtualKeyStates.Down);
		}

		private static VirtualKeyModifiers GetCurrentModifierKeys() {
			VirtualKeyModifiers modifiers = VirtualKeyModifiers.None;

			if (IsKeyPressed(VirtualKey.Shift))
				modifiers |= VirtualKeyModifiers.Shift;

			if (IsKeyPressed(VirtualKey.Control))
				modifiers |= VirtualKeyModifiers.Control;

			if (IsKeyPressed(VirtualKey.Menu)) // Alt key
				modifiers |= VirtualKeyModifiers.Menu;

			if (IsKeyPressed(VirtualKey.LeftWindows) || IsKeyPressed(VirtualKey.RightWindows))
				modifiers |= VirtualKeyModifiers.Windows;

			return modifiers;
		}
		/// <summary>
		/// </summary>
		/// <param name="e"></param>
		protected void OnKeyDown(object sender, KeyRoutedEventArgs e) {
			if (LastKeyboardMessage == null)
				return;

			var modifiers = GetCurrentModifierKeys();

			var msg = LastKeyboardMessage.Message;
			bool handled = ((IKeyboardInputSink)this).TranslateAccelerator(ref msg, modifiers);

			if (handled)
				e.Handled = handled;

			//base.OnKeyDown(e);
		}

		protected void OnKeyUp(object sender, KeyRoutedEventArgs e) {
			if (LastKeyboardMessage == null)
				return;
			var modifiers = GetCurrentModifierKeys();

			var msg = LastKeyboardMessage.Message;
			bool handled = ((IKeyboardInputSink)this).TranslateAccelerator(ref msg, modifiers);

			if (handled)
				e.Handled = handled;

			//base.OnKeyUp(e);
		}
		protected override void OnProcessKeyboardAccelerators(ProcessKeyboardAcceleratorEventArgs args) {
			base.OnProcessKeyboardAccelerators(args);
		}

		#region IKeyboardInputSink

		// General security note on the implementation pattern of this interface. In Dev10 it was chosen
		// to expose the interface implementation for overriding to customers. We did so by keeping the
		// explicit interface implementations (that do have the property of being hidden from the public
		// contract, which limits IntelliSense on derived types like WebBrowser) while sticking protected
		// virtuals next to them. Those virtuals contain our base implementation, while the explicit
		// interface implementation methods do call trivially into the virtuals.
		//
		// This comment outlines the security rationale applied to those methods.
		//
		// <SecurityNote Name="IKeyboardInputSink_Implementation">
		//     The security attributes on the virtual methods within this region mirror the corresponding
		//     IKeyboardInputSink methods; customers can override those methods, so we insert a LinkDemand
		//     to encourage them to have a LinkDemand too (via FxCop).

		/// <summary>
		///     Registers a IKeyboardInputSink with the HwndSource in order
		///     to retreive a unique IKeyboardInputSite for it.
		/// </summary>
		protected virtual IKeyboardInputSite RegisterKeyboardInputSinkCore(IKeyboardInputSink sink) {
			throw new InvalidOperationException("HwndHostDoesNotSupportChildKeyboardSinks");
		}

		IKeyboardInputSite IKeyboardInputSink.RegisterKeyboardInputSink(IKeyboardInputSink sink) {
			return RegisterKeyboardInputSinkCore(sink);
		}

		/// <summary>
		///     Gives the component a chance to process keyboard input.
		///     Return value is true if handled, false if not.  Components
		///     will generally call a child component's TranslateAccelerator
		///     if they can't handle the input themselves.  The message must
		///     either be WM_KEYDOWN or WM_SYSKEYDOWN.  It is illegal to
		///     modify the MSG structure, it's passed by reference only as
		///     a performance optimization.
		/// </summary>
		protected virtual bool TranslateAcceleratorCore(ref Message msg, VirtualKeyModifiers modifiers) {

			return false;
		}

		bool IKeyboardInputSink.TranslateAccelerator(ref Message msg, VirtualKeyModifiers modifiers) {
			return TranslateAcceleratorCore(ref msg, modifiers);
		}

		/// <summary>
		///     Set focus to the first or last tab stop (according to the
		///     TraversalRequest).  If it can't, because it has no tab stops,
		///     the return value is false.
		/// </summary>
		protected virtual bool TabIntoCore(TraversalRequest request) {
			return false;
		}

		bool IKeyboardInputSink.TabInto(TraversalRequest request) {
			return TabIntoCore(request);
		}

		/// <summary>
		///     The property should start with a null value.  The component's
		///     container will set this property to a non-null value before
		///     any other methods are called.  It may be set multiple times,
		///     and should be set to null before disposal.
		/// </summary>
		IKeyboardInputSite IKeyboardInputSink.KeyboardInputSite { get; set; }

		/// <summary>
		///     This method is called whenever one of the component's
		///     mnemonics is invoked.  The message must either be WM_KEYDOWN
		///     or WM_SYSKEYDOWN.  It's illegal to modify the MSG structrure,
		///     it's passed by reference only as a performance optimization.
		///     If this component contains child components, the container
		///     OnMnemonic will need to call the child's OnMnemonic method.
		/// </summary>
		protected virtual bool OnMnemonicCore(ref Message msg, VirtualKeyModifiers modifiers) {
			return false;
		}

		bool IKeyboardInputSink.OnMnemonic(ref Message msg, VirtualKeyModifiers modifiers) {
			return OnMnemonicCore(ref msg, modifiers);
		}

		/// <summary>
		///     Gives the component a chance to process keyboard input messages
		///     WM_CHAR, WM_SYSCHAR, WM_DEADCHAR or WM_SYSDEADCHAR before calling OnMnemonic.
		///     Will return true if "handled" meaning don't pass it to OnMnemonic.
		///     The message must be WM_CHAR, WM_SYSCHAR, WM_DEADCHAR or WM_SYSDEADCHAR.
		///     It is illegal to modify the MSG structure, it's passed by reference
		///     only as a performance optimization.
		/// </summary>
		protected virtual bool TranslateCharCore(ref Message msg, VirtualKeyModifiers modifiers) {
			return false;
		}

		bool IKeyboardInputSink.TranslateChar(ref Message msg, VirtualKeyModifiers modifiers) {
			return TranslateCharCore(ref msg, modifiers);
		}

		/// <summary>
		///     This returns true if the sink, or a child of it, has focus. And false otherwise.
		/// </summary>
		protected virtual bool HasFocusWithinCore() {
			var hwndFocus = PInvoke.GetFocus();
			if (Handle != IntPtr.Zero && (hwndFocus == _hwnd || PInvoke.IsChild(_hwnd, hwndFocus))) {
				return true;
			}
			return false;
		}

		bool IKeyboardInputSink.HasFocusWithin() {
			return HasFocusWithinCore();
		}
		public bool IsVisible => Visibility == Visibility.Visible;
		#endregion IKeyboardInputSink

		/// <summary>
		///     Updates the child window to reflect the state of this element.
		/// </summary>
		/// <remarks>
		///     This includes the size of the window, the position of the
		///     window, and the visibility of the window.
		/// </remarks>
		///<remarks> Not available in Internet zone</remarks>
		public void UpdateWindowPos() {
			// Verify the thread has access to the context.
			// VerifyAccess();

			if (_isDisposed) {
				return;
			}
			// Position the child HWND where layout put it.  To do this we
			// have to get coordinates relative to the parent window.

			if (!XAMLParentWindow.IsNull && this.Parent != null && IsVisible) {
				// Translate the layout information assigned to us from the co-ordinate
				// space of this element, through the root visual, to the Win32 client
				// co-ordinate space
				Rect rcClientRTLAdjusted = GetBoundsRelativeToXAMLWindowRasterScaled();

				// Set the Win32 position for the child window.
				//
				// Track the last rectangle sent to Win32 so we can avoid redundant
				// synchronous SetWindowPos calls. A resize of the terminal renderer
				// must only run after this position update has completed.
				//
				OnWindowPositionChanged(rcClientRTLAdjusted);

				// Show the window
				// Based on Dwayne, the reason we also show/hide window in UpdateWindowPos is for the 
				// following kind of scenario: When applying RenderTransform to HwndHost, the hwnd
				// will be left behind. Developer can workaround by hide the hwnd first using pinvoke. 
				// After the RenderTransform is applied to the HwndHost, call UpdateWindowPos to sync up
				// the hwnd's location, size and visibility with WPF.

				PInvoke.ShowWindowAsync(_hwnd, ActivateOnShow ? SHOW_WINDOW_CMD.SW_SHOW : SHOW_WINDOW_CMD.SW_SHOWNA);
			} else {
				// For some reason we shouldn't be displayed: either we don't
				// have a parent, or the parent no longer has a root visual,
				// or we are marked as not being visible.
				//
				// Just hide the window to get it out of the way.
				PInvoke.ShowWindowAsync(_hwnd, SHOW_WINDOW_CMD.SW_HIDE);
			}
		}
		protected bool ActivateOnShow = true;
		// Translate the layout information assigned to us from the co-ordinate
		// space of this element, through the root visual, to the Win32 client
		// co-ordinate space
		//private RECT CalculateAssignedRC(FrameworkElement source) {
		//	var rectElement = new RECT(RenderSize);
		//	var rectRoot = PointUtil.ElementToRoot(rectElement, this, source);
		//	var rectClient = PointUtil.RootToClient(rectRoot, source);


		//	var rcClient = PointUtil.FromRect(rectClient);
		//	var rcClientRTLAdjusted = rcClient;

		//	return rcClientRTLAdjusted;
		//}
		Rect GetBoundsRelativeToXAMLWindowRasterScaled() {
			if (this.Parent == null || XAMLParentWindow.IsNull)
				throw new Exception("We are not part of the UI or the parent is not yet set");
			var point = GetTransformToXamlRootContent().TransformPoint(new Point(0, 0));
			var rasterizationScale = this.XamlRoot.RasterizationScale;
			point = new Point(point.X * rasterizationScale, point.Y * rasterizationScale);
			var toSize = ActualSize.ToSize();
			toSize = new Size(toSize.Width * rasterizationScale, toSize.Height * rasterizationScale);
			return AdjustRectForDpi(new Rect(point, toSize));
		}

		/// <summary>
		/// Gets the ratio of the DPI between the parent of <see cref="_hwnd"/>
		/// and <see cref="_hwnd"/>. Normally, this ratio is 1. 
		/// </summary>
		private double DpiParentToChildRatio {
			get {
				if (!_hasDpiAwarenessContextTransition)
					return 1.0;
				var windowDpi = PInvoke.GetDpiForWindow(_hwnd);
				var parentDpi = (double)PInvoke.GetDpiForWindow(PInvoke.GetParent(_hwnd));
				return parentDpi / windowDpi;
			}
		}

		/// <summary>
		/// Adjusts a rectangle to factor in the differences in DPI between 
		/// the parent of <see cref="_hwnd"/> and <see cref="_hwnd"/>
		/// </summary>
		/// <param name="rcRect">The rectangle to adjust</param>
		/// <returns>The adjusted rectangle</returns>
		private Rect AdjustRectForDpi(Rect rcRect) {
			if (!_hasDpiAwarenessContextTransition)
				return rcRect;

			double dpiRatio = DpiParentToChildRatio;
			return new(rcRect.X / dpiRatio, rcRect.Y / dpiRatio, rcRect.Width / dpiRatio, rcRect.Height / dpiRatio);
		}

		/// <summary>
		///     Disposes this object.
		/// </summary>
		/// <param name="disposing">
		///     true if called from explisit Dispose; and we free all objects managed and un-managed.
		///     false if called from the finalizer; and we free only un-managed objects.
		/// </param>
		/// <remarks>
		///     Derived classes should override this if they have additional
		///     cleanup to do.  The base class implementation should be called.
		///     Note that the calling thread must be the dispatcher thread.
		///     If a window is being hosted, that window is destroyed.
		/// </remarks>
		protected virtual void Dispose(bool disposing) {
			if (_isDisposed == true) {
				return;
			}


			if (disposing) {
				// Verify the thread has access to the context.
#pragma warning suppress 6519

				this.SizeChanged -= OnSizeChanged;
				this.PreviewKeyDown -= OnKeyDown;
				this.PreviewKeyUp -= OnKeyUp;
				if (_visibilityChangedToken is long visibilityChangedToken) {
					this.UnregisterPropertyChangedCallback(UIElement.VisibilityProperty, visibilityChangedToken);
					_visibilityChangedToken = null;
				}

				if (xamlParentMessageMonitor != null) {
					xamlParentMessageMonitor.WindowMessageReceived -= xamlParentWndProc;
					xamlParentMessageMonitor.Dispose();
					xamlParentMessageMonitor = null;
					_XAMLParentWindow = HWND.Null;
				}



				// Remove our subclass.  Even if this fails, it will be forcably removed
				// when the window is destroyed.
				if (_hwndSubclassHook != null) {
					// Check if it is trusted (WebOC and AddInHost), call CriticalDetach to avoid the Demand.
					_hwndSubclassHook.WindowMessageReceived -= SubclassWndProc;
					_hwndSubclassHook.Dispose();

					_hwndSubclassHook = null;
				}


				// We no longer need to know about the source changing.
				//PresentationSource.RemoveSourceChangedHandler(this, new SourceChangedEventHandler(OnSourceChanged));
			}

			if (_weakEventDispatcherShutdown != null) // Can be null if the static ctor failed ... see WebBrowser.
			{
				_weakEventDispatcherShutdown.Dispose();
				_weakEventDispatcherShutdown = null;
			}

			DestroyWindow();

			_isDisposed = true;
		}

		private void OnDispatcherShutdown(object sender, object e) {
			Dispose();
		}

		/// <summary>
		///     Derived classes override this method to actually build the
		///     window being hosted.
		/// </summary>
		/// <param name="hwndParent">
		///     The parent HWND for the child window.
		/// </param>
		/// <returns>
		///     The HWND handle to the child window that was created.
		/// </returns>
		/// <remarks>
		///     The window that is returned must be a child window of the
		///     specified parent window.
		///     <para/>
		///     In addition, the child window will only be subclassed if
		///     the window is owned by the calling thread.
		/// </remarks>
		protected abstract HWND BuildWindowCore(HWND hwndParent);

		/// <summary>
		///     Derived classes override this method to destroy the
		///     window being hosted.
		/// </summary>
		protected abstract void DestroyWindowCore(HWND hwnd);

		/// <summary>
		/// Called when the hosted HWND is destroyed outside <see cref="DestroyWindowCore"/>.
		/// Implementations can invalidate native state without destroying the HWND
		/// reentrantly from inside WM_NCDESTROY.
		/// </summary>
		protected virtual void OnHostedWindowDestroyed(HWND hwnd) {
		}

		/// <summary>
		/// Called after the hosted HWND has accepted its XAML layout rectangle.
		/// </summary>
		protected virtual void OnHostedWindowPositionApplied(int width, int height) {
		}



		/// <summary>
		///     A protected override for accessing the window proc of the
		///     hosted child window.
		/// </summary>
		///<remarks> Not available in Internet zone</remarks>
		protected virtual void WndProc(WindowMessageEventArgs e) {
			Debug.WriteLine($"Child got message {(WindowsMessages)e.Message.MessageId}: {e.Message}");
			DemandIfUntrusted();
			var msg = e.Message;

			switch ((WindowsMessages)e.Message.MessageId) {
				case WindowsMessages.NCDESTROY: {
					var destroyedHwnd = _hwnd == HWND.Null ? new HWND(msg.Hwnd) : _hwnd;
					_hwnd = HWND.Null;
					InvalidateWindowPositionCache();
					_hasAppliedWindowRegion = false;
					_lastRegionLeft = -1;
					_lastRegionWidth = -1;
					_lastRegionHeight = -1;
					OnHostedWindowDestroyed(destroyedHwnd);
					break;
				}

				// When layout happens, we first calculate the right size/location then call SetWindowPos.
				// We only allow the changes that are coming from Avalon layout. The hwnd is not allowed to change by itself.
				// So the size of the hwnd should always be RenderSize and the position be where layout puts it.
				case WindowsMessages.WINDOWPOSCHANGING:

					if (!XAMLParentWindow.IsNull && this.Parent != null) {
						// Get the rect assigned by layout to us.
						Rect assignedRC = GetBoundsRelativeToXAMLWindowRasterScaled();

						// The lParam is a pointer to a WINDOWPOS structure
						// that contains information about the size and
						// position that the window is changing to.  Note that
						// modifying this structure during WM_WINDOWPOSCHANGING
						// will change what happens to the window.
						unsafe {
							WINDOWPOS* windowPos = (WINDOWPOS*)msg.LParam;

							// Always force the size of the window to be the
							// size of our assigned rectangle.  Note that we
							// have to always clear the SWP_NOSIZE flag.
							windowPos->cx = (int)(assignedRC.Right - assignedRC.Left);
							windowPos->cy = (int)(assignedRC.Bottom - assignedRC.Top);
							windowPos->flags &= ~SET_WINDOW_POS_FLAGS.SWP_NOSIZE;

							// Always force the position of the window to be
							// the upper-left corner of our assigned rectangle.
							// Note that we have to always clear the
							// SWP_NOMOVE flag.
							windowPos->x = (int)(assignedRC.Left);
							windowPos->y = (int)(assignedRC.Top);
							windowPos->flags &= ~SET_WINDOW_POS_FLAGS.SWP_NOMOVE;

							// Windows has an optimization to copy pixels
							// around to reduce the amount of repainting
							// needed when moving or resizing a window.
							// Unfortunately, this is not compatible with WPF
							// in many cases due to our use of DirectX for
							// rendering from our rendering thread.
							// To be safe, we disable this optimization and
							// pay the cost of repainting.
							windowPos->flags |= SET_WINDOW_POS_FLAGS.SWP_NOCOPYBITS;
						}
					}

					break;


				case WindowsMessages.GETOBJECT:
					e.Handled = true;
					e.Result = OnWmGetObject((nint)msg.WParam, msg.LParam);
					break;
			}

		}

		#region Automation

		/// <summary>
		/// Creates AutomationPeer (<see cref="UIElement.OnCreateAutomationPeer"/>)
		/// </summary>
		protected override AutomationPeer OnCreateAutomationPeer() {
			return new HwndHostAutomationPeer(this);
		}

		private IntPtr OnWmGetObject(IntPtr wparam, IntPtr lparam) {
			IntPtr result = IntPtr.Zero;

			var containerPeer = FrameworkElementAutomationPeer.FromElement(this);
			if (containerPeer != null) {
				// get the element proxy
				//todo
				//IRawElementProviderSimple el = containerPeer.GetInteropChild();
				//result = AutomationInteropProvider.ReturnRawElementProvider(Handle, wparam, lparam, el);
			}
			return result;
		}

		#endregion Automation

		// Make this protected virtual when enabling the WebOC code.
		//NEEDS final signoff from the owning team.
		/// <summary>
		/// Called when the window rect changes. Subclasses can override this to
		/// update child window's Rect using these new coordinates.
		/// </summary>
		/// <param name="rcBoundingBox"></param>
		protected virtual void OnWindowPositionChanged(Rect rcBoundingBox) {
			if (_isDisposed || _isUpdatingWindowPosition || _hwnd == HWND.Null || !PInvoke.IsWindow(_hwnd)) {
				return;
			}

			_isUpdatingWindowPosition = true;
			try {
				var x = (int)rcBoundingBox.X;
				var y = (int)rcBoundingBox.Y;
				var width = Math.Max(0, (int)rcBoundingBox.Width);
				var height = Math.Max(0, (int)rcBoundingBox.Height);
				if (_hasLastWindowPosition &&
					_lastWindowX == x &&
					_lastWindowY == y &&
					_lastWindowWidth == width &&
					_lastWindowHeight == height) {
					ApplyWindowRegion(rcBoundingBox);
					return;
				}

				// HWND_BOTTOM controls the z-order only among HWND siblings. XAML siblings are
				// compositor content, so they still suffer from HwndHost airspace. Keep the
				// renderer at the bottom of the HWND sibling order, then clip the portion that
				// is covered by the left-side XAML overlay.
				NativeWindowResizeOrder.Apply(
					() => PInvoke.SetWindowPos(_hwnd,
						new HWND(1),
						x,
						y,
						width,
						height,
						SET_WINDOW_POS_FLAGS.SWP_NOCOPYBITS
						| SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE),
					() => {
						_hasLastWindowPosition = true;
						_lastWindowX = x;
						_lastWindowY = y;
						_lastWindowWidth = width;
						_lastWindowHeight = height;
						OnHostedWindowPositionApplied(width, height);
					});

				ApplyWindowRegion(rcBoundingBox);
			} finally {
				_isUpdatingWindowPosition = false;
			}
		}

		private void InvalidateWindowPositionCache() {
			_hasLastWindowPosition = false;
		}

		private void ApplyWindowRegion(Rect rcBoundingBox) {
			if (_hwnd.IsNull) {
				return;
			}

			var width = Math.Max(0, (int)Math.Ceiling(rcBoundingBox.Width));
			var height = Math.Max(0, (int)Math.Ceiling(rcBoundingBox.Height));
			var clipLeft = 0;

			if (_leftOverlayBoundaryInXamlRootDips is double boundary && XamlRoot != null) {
				var boundaryInChildPixels = boundary * XamlRoot.RasterizationScale;
				if (_hasDpiAwarenessContextTransition) {
					boundaryInChildPixels /= DpiParentToChildRatio;
				}

				clipLeft = Math.Clamp(
					(int)Math.Ceiling(boundaryInChildPixels - rcBoundingBox.X),
					0,
					width);
			}

			if (clipLeft <= 0) {
				if (_hasAppliedWindowRegion) {
					SetWindowRgn(_hwnd.Value, IntPtr.Zero, true);
					_hasAppliedWindowRegion = false;
				}
				_lastRegionLeft = 0;
				_lastRegionWidth = width;
				_lastRegionHeight = height;
				return;
			}

			if (_hasAppliedWindowRegion &&
				_lastRegionLeft == clipLeft &&
				_lastRegionWidth == width &&
				_lastRegionHeight == height) {
				return;
			}

			var region = CreateRectRgn(clipLeft, 0, width, height);
			if (region == IntPtr.Zero) {
				return;
			}

			// After a successful SetWindowRgn call USER32 owns the HRGN.
			if (SetWindowRgn(_hwnd.Value, region, true) == 0) {
				DeleteObject(region);
				return;
			}

			_hasAppliedWindowRegion = true;
			_lastRegionLeft = clipLeft;
			_lastRegionWidth = width;
			_lastRegionHeight = height;
		}

		/// <summary>
		///     Return the desired size of the HWND.
		/// </summary>
		/// <remarks>
		///     HWNDs usually expect a very simplisitic layout model where
		///     a window gets to be whatever size it wants to be.  To respect
		///     this we request the initial size that the window was created
		///     at.  A window created with a 0 dimension will adopt whatever
		///     size the containing layout wants it to be.  Layouts are free
		///     to actually size the window to whatever they want, and the
		///     child window will always be sized accordingly.
		///     <para/>
		///     Derived classes should only override this method if they
		///     have special knowlege about the size the window wants to be.
		///     Examples of such may be special HWND types like combo boxes.
		///     In such cases, the base class must still be called, but the
		///     return value can be changed appropriately.
		/// </remarks>
		///<remarks> Not available in Internet zone</remarks>
		protected override Size MeasureOverride(Size constraint) {
			DemandIfUntrusted();

			Size desiredSize = new Size(0, 0);

			// Measure to our desired size.  If we have a 0-length dimension,
			// the system will assume we don't care about that dimension.
			if (Handle != IntPtr.Zero) {
				desiredSize.Width = Math.Min(_desiredSize.Width, constraint.Width);
				desiredSize.Height = Math.Min(_desiredSize.Height, constraint.Height);
			}

			return desiredSize;
		}
		protected override void OnDisconnectVisualChildren() {

			base.OnDisconnectVisualChildren();
		}
		private void Initialize(bool fTrusted) {
			IsTabStop = true;
			_fTrusted = fTrusted;

			_weakEventDispatcherShutdown = new WeakEventDispatcherShutdown(this, this.DispatcherQueue);



			this.SizeChanged += OnSizeChanged;
			this.PreviewKeyDown += OnKeyDown;
			this.PreviewKeyUp += OnKeyUp;
			_visibilityChangedToken = this.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, OnVisibleChanged);


		}





		///<summary>
		///     Use this method as a defense-in-depth measure only.
		///</summary>
		private void DemandIfUntrusted() {
			if (!_fTrusted) {
			}
		}

		private void OnSourceChanged(object sender) {
			// Remove ourselves as an IKeyboardInputSinks child of our previous
			// containing window.
			IKeyboardInputSite keyboardInputSite = ((IKeyboardInputSink)this).KeyboardInputSite;
			if (keyboardInputSite != null) {
				// Derived classes that implement IKeyboardInputSink should support setting it to null.
				((IKeyboardInputSink)this).KeyboardInputSite = null;

				keyboardInputSite.Unregister();
			}

			//Add ourselves as an IKeyboardInputSinks child of our containing window.
			//IKeyboardInputSink source = PresentationSource.CriticalFromVisual(this, false /* enable2DTo3DTransition */) as IKeyboardInputSink;
			//if (source != null) {
			//	((IKeyboardInputSink)this).KeyboardInputSite = source.RegisterKeyboardInputSink(this);
			//}

			BuildOrReparentWindow();
		}

		private void OnSizeChanged(object sender, SizeChangedEventArgs e) {
			var hasValidSize = e.NewSize.Width > 0 && e.NewSize.Height > 0;
			if (!NativeWindowPositionUpdatePolicy.ShouldQueue(
				_isDisposed,
				_isUpdatingWindowPosition,
				hasValidSize,
				_lastXamlSizeForWindowPosition == e.NewSize)) {
				return;
			}

			_lastXamlSizeForWindowPosition = e.NewSize;
			QueueWindowPositionUpdate();
		}

		private void QueueWindowPositionUpdate() {
			if (_isDisposed || _windowPositionUpdateQueued) {
				return;
			}

			_windowPositionUpdateQueued = true;
			try {
				if (DispatcherQueue.TryEnqueue(() => {
					_windowPositionUpdateQueued = false;
					if (_isDisposed) {
						return;
					}

					UpdateWindowPos();
				})) {
					return;
				}
			} catch (Exception ex) {
				Debug.WriteLine($"Unable to queue hosted window position update: {ex}");
			}

			_windowPositionUpdateQueued = false;
		}

		private void OnEnabledChanged(object sender, DependencyPropertyChangedEventArgs e) {
			if (_isDisposed) {
				return;
			}

			bool boolNewValue = (bool)e.NewValue;
			PInvoke.EnableWindow(_hwnd, boolNewValue);
		}


		private void OnVisibleChanged(DependencyObject sender, DependencyProperty dp) {
			if (_isDisposed) {
				return;
			}

			bool vis = (bool)IsVisible;

			// BUG 148548 HwndHost does not always repaint on restore from minimize.
			// We used to call ShowWindow here and ShowWindowAsync in other places (UpdateWindowPos). 
			// The inconsistent sync/async showing window causes the repainting bug. 
			// There was recollection from Dwayne that ShowWindow sync might cause rereentrancy issues.
			// So change here to show async to be consistent with everywhere else (instead of changing everywhere else
			// to show window sync).            
			if (vis)
				PInvoke.ShowWindowAsync(_hwnd, ActivateOnShow ? SHOW_WINDOW_CMD.SW_SHOW : SHOW_WINDOW_CMD.SW_SHOWNA);
			else
				PInvoke.ShowWindowAsync(_hwnd, SHOW_WINDOW_CMD.SW_HIDE);

			QueueWindowPositionUpdate();
		}

		// This routine handles the following cases:
		// 1) a parent window is present, build the child window
		// 2) a parent is present, reparent the child window to it
		// 3) a parent window is not present, hide the child window by parenting it to SystemResources.Hwnd window.
		private void BuildOrReparentWindow() {
			DemandIfUntrusted();

			// Verify the thread has access to the context.
			// VerifyAccess();

			// Prevent reentry while building a child window,
			// also prevent the reconstruction of Disposed objects.
			if (_isBuildingWindow || _isDisposed) {
				return;
			}

			_isBuildingWindow = true;

			// Find the source window, this must be the parent window of
			// the child window.


			try {
				if (!XAMLParentWindow.IsNull) {
					if (_hwnd == IntPtr.Zero) {

						// We now have a parent window, so we can create the child
						// window.
						BuildWindow(XAMLParentWindow);
						//this.IsEnabledChanged += _handlerEnabledChanged;
						//this.propertych += _handlerVisibleChanged;
					} else if (XAMLParentWindow != PInvoke.GetParent(_hwnd)) {
						// We have a different parent window.  Just reparent the
						// child window under the new parent window.
						PInvoke.SetParent(_hwnd, XAMLParentWindow);
						InvalidateWindowPositionCache();
					}
				} else if (Handle != IntPtr.Zero) {
					// Reparent the window to notification-only window provided by SystemResources
					// This keeps the child window around, but it is not visible.  We can reparent the 
					// window later when a new parent is available
					var hwnd = XAMLParentWindow;//
					Debug.Assert(hwnd != HWND.Null);
					if (!hwnd.IsNull) {
						PInvoke.SetParent(_hwnd, hwnd);
						// ...But we have a potential problem: If the SystemResources listener window gets 
						// destroyed ahead of the call to HwndHost.OnDispatcherShutdown(), the HwndHost's window
						// will be destroyed too, before the "logical" Dispose has had a chance to do proper
						// shutdown. This turns out to be very significant for WebBrowser/ActiveXHost, which shuts
						// down the hosted control through the COM interfaces, and the control destroys its
						// window internally. Evidently, the WebOC fails to do full, proper cleanup if its
						// window is destroyed unexpectedly.
						// To avoid this situation, we make sure SystemResources responds to the Dispatcher 
						// shutdown event after this HwndHost.
						//todo maybe
						//SystemResources.DelayHwndShutdown();
					} else {
						Trace.WriteLineIf(hwnd == null, $"- Warning - Notification Window is null\n{new System.Diagnostics.StackTrace(true).ToString()}");
					}
				}
			} finally {
				// Be careful to clear our guard bit.
				_isBuildingWindow = false;
			}
		}

		private string HwndStr(HWND? hwnd) => hwnd == null || hwnd.Value.IsNull ? "null" : ((uint)(hwnd.Value.Value)).ToString();
		private string getHwndsWeKnow() => $"XAMLParentWindow: {HwndStr(XAMLParentWindow)} hosted: {HwndStr(_hwnd)}";

		private unsafe void BuildWindow(HWND hwndParent) {
			// Demand unmanaged code to the caller. IT'S RISKY TO REMOVE THIS
			DemandIfUntrusted();

			// Allow the derived class to build our HWND.
			InvalidateWindowPositionCache();
			_hwnd = BuildWindowCore(hwndParent);
			_hasAppliedWindowRegion = false;
			_lastRegionLeft = -1;
			_lastRegionWidth = -1;
			_lastRegionHeight = -1;
			if (_hwnd == IntPtr.Zero || !PInvoke.IsWindow(_hwnd)) {
				throw new InvalidOperationException("ChildWindowNotCreated");
			}

			// Make sure that the window that was created is indeed a child window.
			var windowStyle = (WINDOW_STYLE)PInvoke.GetWindowLong(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
			if ((windowStyle & WINDOW_STYLE.WS_CHILD) == 0) {
				throw new InvalidOperationException("HostedWindowMustBeAChildWindow");
			}

			// Make sure the child window is the child of the expected parent window.
			if (hwndParent != PInvoke.GetParent(_hwnd)) {
				throw new InvalidOperationException("ChildWindowMustHaveCorrectParent");
			}

			// Test to see if hwndParent and _hwnd have different DPI_AWARENESS_CONTEXT's

			if (PInvoke.GetDpiForWindow(_hwnd) != PInvoke.GetDpiForWindow(hwndParent)) {
				_hasDpiAwarenessContextTransition = true;
			}

			// Only subclass the child HWND if it is owned by our thread.
			uint idWindowProcess = 0;
			uint* pIdWindowProcess = &idWindowProcess;
			uint idWindowThread = PInvoke.GetWindowThreadProcessId(_hwnd, pIdWindowProcess);


			if ((idWindowThread == PInvoke.GetCurrentThreadId()) &&
				(idWindowProcess == Environment.ProcessId)) {
				_hwndSubclassHook = new WindowMessageMonitor(_hwnd);
				_hwndSubclassHook.WindowMessageReceived += SubclassWndProc;
			}

			// Initially make sure the window is hidden.  We will show it later during rendering.
			PInvoke.ShowWindowAsync(_hwnd, SHOW_WINDOW_CMD.SW_HIDE);

			// Assume the desired size is the initial size.  If the window was
			// created with a 0-length dimension, we assume this means we
			// should fill all available space.

			PInvoke.GetWindowRect(_hwnd, out var rc);

			// Convert from pixels to measure units.
			// PresentationSource can't be null if we get here.

			Point ptUpperLeft = new Point(rc.left, rc.top);
			Point ptLowerRight = new Point(rc.right, rc.bottom);
			ptUpperLeft = GetTransformToXamlRootContent().TransformPoint(ptUpperLeft);
			ptLowerRight = GetTransformToXamlRootContent().TransformPoint(ptLowerRight);
			_desiredSize = new Size(ptLowerRight.X - ptUpperLeft.X, ptLowerRight.Y - ptUpperLeft.Y);

			// We have a new desired size, so invalidate measure.
			InvalidateMeasure();
		}



		private void DispatcherInvoke(Action action, Microsoft.UI.Dispatching.DispatcherQueuePriority priority = Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal) =>
			UWPHelpers.Enqueue(this.DispatcherQueue, action, priority);

		private bool CheckAccess() => this.DispatcherQueue.HasThreadAccess;
		private void DestroyWindow() {
			// Destroy the window if we are hosting one.
			if (Handle == IntPtr.Zero)
				return;

			if (!CheckAccess()) {
				// I understand we can get in here on the finalizer thread.  And
				// touching other GC'ed objects in the finalizer is typically bad.
				// But a Context object can be accessed after finalization.
				// We need to touch the Context to switch to the right thread.
				// If the Context has been finalized then we won't get switched
				// and that is OK.
				DispatcherInvoke(() => AsyncDestroyWindow(null));
				return;
			}

			var hwnd = _hwnd;
			_hwnd = HWND.Null;
			InvalidateWindowPositionCache();

			DestroyWindowCore(hwnd);
		}

		private object AsyncDestroyWindow(object arg) {
			DestroyWindow();
			return null;
		}
		private void SubclassWndProc(object sender, WindowMessageEventArgs e) {
			WndProc(e);
			if (!e.Handled)
				MessageHook?.Invoke(sender, e);
		}







		private HWND _hwnd;

		private Size _desiredSize;
		private bool _isUpdatingWindowPosition;
		private bool _windowPositionUpdateQueued;
		private Size _lastXamlSizeForWindowPosition;
		private bool _hasLastWindowPosition;
		private int _lastWindowX;
		private int _lastWindowY;
		private int _lastWindowWidth;
		private int _lastWindowHeight;

		/// <summary>
		/// True when the parent of <see cref="_hwnd"/> and <see cref="_hwnd"/>
		/// have different DPI_AWARENESS_CONTEXT values. This indicates that 
		/// DPI transitions are possible in content hosted by this <see cref="HwndHost"/>. 
		/// </summary>
		private bool _hasDpiAwarenessContextTransition = false;

		private bool _fTrusted;

		private bool _isBuildingWindow = false;

		private bool _isDisposed = false;

		private class WeakEventDispatcherShutdown : WeakReference {
			public WeakEventDispatcherShutdown(HwndHost hwndHost, DispatcherQueue that) : base(hwndHost) {
				_that = that;
				_that.ShutdownCompleted += OnShutdownFinished;
			}

			public void OnShutdownFinished(DispatcherQueue sender, object args) {
				HwndHost hwndHost = this.Target as HwndHost;
				if (null != hwndHost) {
					hwndHost.OnDispatcherShutdown(sender, args);
				} else {
					Dispose();
				}
			}

			public void Dispose() {
				if (null != _that) {
					_that.ShutdownCompleted -= OnShutdownFinished;
				}
			}

			private DispatcherQueue _that;
		}
		WeakEventDispatcherShutdown _weakEventDispatcherShutdown;
	}

}
