// <copyright file="TerminalControl.xaml.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
// </copyright>
using System;
using System.Drawing;
using System.Threading;


using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using UIColor = Windows.UI.Color;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation.Peers;
using Windows.Win32.UI.WindowsAndMessaging;
using Windows.Win32;
using Microsoft.Terminal.WinUI3;
using System.Diagnostics;
namespace Microsoft.Terminal.Wpf {


	using Task = System.Threading.Tasks.Task;

	/// <summary>
	/// A basic terminal control. This control can receive and render standard VT100 sequences.
	/// </summary>
	public partial class TerminalControl : UserControl {
		private int accumulatedDelta = 0;
		public TerminalControl() {

			this.InitializeComponent();
			Init();
			

		}

		private async void TerminalControl_GettingFocus(UIElement sender, GettingFocusEventArgs args) {
			args.Cancel=true;
			termContainer.PassFocus();			
		}

		private static int? _ScrollLines;
		private static unsafe int ScrollLines {
			get {
				if (_ScrollLines == null) {
					uint scrollLines;
					_ScrollLines = PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETWHEELSCROLLLINES, 0, &scrollLines, 0);
					_ScrollLines = (int)scrollLines;
				}
				return _ScrollLines.Value;
			}
		}


		private void Init() {

			this.termContainer.TerminalScrolled += this.TermControl_TerminalScrolled;
			this.termContainer.UserScrolled += this.TermControl_UserScrolled;
			this.scrollbar.Scroll += this.Scrollbar_Scroll;

			this.SizeChanged += this.TerminalControl_SizeChanged;

			this.PointerWheelChanged += MouseWheelChanged;

			this.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, OnVisibleChanged);
			IsTabStop=true;
			this.GettingFocus += TerminalControl_GettingFocus;
		}

		private void OnVisibleChanged(DependencyObject sender, DependencyProperty dp) {
			termContainer.Visibility = Visibility;
		}

		private void MouseWheelChanged(object sender, PointerRoutedEventArgs e) {
			var delta = e.GetCurrentPoint(this)?.Properties.MouseWheelDelta;
			if (delta == null)
				return;
			this.TermControl_UserScrolled(sender, delta.Value);
		}

		/// <summary>
		/// Gets the current character rows available to the terminal.
		/// </summary>
		public int Rows => this.termContainer.Rows;

		/// <summary>
		/// Gets the current character columns available to the terminal.
		/// </summary>
		public int Columns => this.termContainer.Columns;

		/// <summary>
		/// Gets or sets a value indicating whether if the renderer should automatically resize to fill the control
		/// on user action.
		/// </summary>
		public bool AutoResize {
			get => this.termContainer.AutoResize;
			set => this.termContainer.AutoResize = value;
		}

		/// <summary>
		/// Sets the connection to a terminal backend.
		/// </summary>
		public ITerminalConnection Connection {
			set => this.termContainer.Connection = value;
		}

		/// <summary>
		/// Gets size of the terminal renderer.
		/// </summary>
		private Size TerminalRendererSize {
			get => this.termContainer.TerminalRendererSize;
		}
		private UIColor FromDrawingColor(Color color) => UIColor.FromArgb(color.A, color.R, color.G, color.B);
		/// <summary>
		/// Sets the theme for the terminal. This includes font family, size, color, as well as background and foreground colors.
		/// </summary>
		/// <param name="theme">The color theme to use in the terminal.</param>
		/// <param name="fontFamily">The font family to use in the terminal.</param>
		/// <param name="fontSize">The font size to use in the terminal.</param>
		/// <param name="externalBackground">Color for the control background when the terminal window is smaller than the hosting WPF window.</param>
		public void SetTheme(TerminalTheme theme, string fontFamily, short fontSize, Color externalBackground = default) {

			if (termContainer.Hwnd == 0)
				return;
			this.termContainer.SetTheme(theme, fontFamily, fontSize);

			// DefaultBackground uses Win32 COLORREF syntax which is BGR instead of RGB.
			byte b = Convert.ToByte((theme.DefaultBackground >> 16) & 0xff);
			byte g = Convert.ToByte((theme.DefaultBackground >> 8) & 0xff);
			byte r = Convert.ToByte(theme.DefaultBackground & 0xff);

			// Set the background color for the control only if one is provided.
			// This is only shown when the terminal renderer is smaller than the enclosing WPF window.
			if (externalBackground != default) {
				this.Background = new SolidColorBrush(FromDrawingColor(externalBackground));
			}
		}

		/// <summary>
		/// Gets the selected text in the terminal, clearing the selection. Otherwise returns an empty string.
		/// </summary>
		/// <returns>Selected text, empty string if no content is selected.</returns>
		public string GetSelectedText() {
			return this.termContainer.GetSelectedText();
		}

		/// <summary>
		/// Resizes the terminal to the specified rows and columns.
		/// </summary>
		/// <param name="rows">Number of rows to display.</param>
		/// <param name="columns">Number of columns to display.</param>
		/// <param name="cancellationToken">Cancellation token for this task.</param>
		/// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
		public async Task ResizeAsync(uint rows, uint columns, CancellationToken cancellationToken) {
			this.termContainer.Resize(rows, columns);

#pragma warning disable VSTHRD001 // Avoid legacy thread switching APIs
			await RunAsync(
				new Action(delegate {
					this.terminalGrid.Margin = this.CalculateMargins();
				})
				);
#pragma warning restore VSTHRD001 // Avoid legacy thread switching APIs
		}
		public Task RunAsync(Action action) => UWPHelpers.Enqueue(this.DispatcherQueue, action);
		public double DPIScale => termContainer.curDPI.DpiScaleX;
		/// <summary>
		/// Resizes the terminal to the specified dimensions.
		/// </summary>
		/// <param name="rendersize">Rendering size for the terminal in device independent units.</param>
		/// <returns>A tuple of (int, int) representing the number of rows and columns in the terminal.</returns>
		public (int rows, int columns) TriggerResize(Size rendersize) {
			rendersize.Width = (int)(DPIScale * rendersize.Width);
			rendersize.Height = (int)(DPIScale * rendersize.Height);

			if (rendersize.Width == 0 || rendersize.Height == 0) {
				return (0, 0);
			}

			this.termContainer.Resize(rendersize);

			return (this.Rows, this.Columns);
		}

		/// <inheritdoc/>
		protected override AutomationPeer OnCreateAutomationPeer() {
			var peer = FrameworkElementAutomationPeer.FromElement(this);
			if (peer == null) {
				// Provide our own automation peer here that just sets IsContentElement/IsControlElement to false
				// (aka AccessibilityView = Raw). This makes it not pop up in the UIA tree.
				peer = new TermControlAutomationPeer(this);
			}

			return peer;
		}



		/// <inheritdoc/>
		private void TerminalControl_SizeChanged(object sender, SizeChangedEventArgs sizeInfo) {

			var newSizeWidth = (sizeInfo.NewSize.Width - this.scrollbar.ActualWidth) * DPIScale;
			newSizeWidth = newSizeWidth < 0 ? 0 : newSizeWidth;

			var newSizeHeight = sizeInfo.NewSize.Height * DPIScale;
			newSizeHeight = newSizeHeight < 0 ? 0 : newSizeHeight;

			this.termContainer.TerminalControlSize = new Size {
				Width = (int)newSizeWidth,
				Height = (int)newSizeHeight,
			};

			if (!this.AutoResize) {
				// Renderer will not resize on control resize. We have to manually calculate the margin to fill in the space.
				terminalGrid.Margin = CalculateMargins(new Size((int)sizeInfo.NewSize.Width, (int)sizeInfo.NewSize.Height));

				// Margins stop resize events, therefore we have to manually check if more space is available and raise
				//  a resize event if needed.
				this.termContainer.RaiseResizedIfDrawSpaceIncreased();
			}

			//base.OnRenderSizeChanged(sizeInfo);
		}


		/// <summary>
		/// Calculates the margins that should surround the terminal renderer, if any.
		/// </summary>
		/// <param name="controlSize">New size of the control. Uses the control's current size if not provided.</param>
		/// <returns>The new terminal control margin thickness in device independent units.</returns>
		private Thickness CalculateMargins(Size controlSize = default) {
			double width = 0, height = 0;

			if (controlSize == default) {
				controlSize = new Size {
					Width = (int)this.terminalUserControl.ActualWidth,
					Height = (int)this.terminalUserControl.ActualHeight,
				};
			}

			// During initialization, the terminal renderer size will be 0 and the terminal renderer
			// draws on all available space. Therefore no margins are needed until resized.
			if (this.TerminalRendererSize.Width != 0) {
				width = controlSize.Width - (this.TerminalRendererSize.Width / DPIScale);
			}

			if (this.TerminalRendererSize.Height != 0) {
				height = controlSize.Height - (this.TerminalRendererSize.Height / DPIScale);
			}

			width -= this.scrollbar.ActualWidth;

			// Prevent negative margin size.
			width = width < 0 ? 0 : width;
			height = height < 0 ? 0 : height;

			return new Thickness(0, 0, width, height);
		}


		private async void TermControl_UserScrolled(object sender, int delta) {

			var lineDelta = 120 / ScrollLines;
			this.accumulatedDelta += delta;

			if (this.accumulatedDelta < lineDelta && this.accumulatedDelta > -lineDelta) {
				return;
			}

			await RunAsync(() => {
				var lines = -this.accumulatedDelta / lineDelta;
				this.scrollbar.Value += lines;
				this.accumulatedDelta = 0;

				this.termContainer.UserScroll((int)this.scrollbar.Value);
			});
		}

		private async void TermControl_TerminalScrolled(object sender, (int viewTop, int viewHeight, int bufferSize) e) {
			await RunAsync(() => {
				this.scrollbar.Minimum = 0;
				this.scrollbar.Maximum = e.bufferSize - e.viewHeight;
				this.scrollbar.Value = e.viewTop;
				this.scrollbar.ViewportSize = e.viewHeight;
			});
		}
		private void Scrollbar_Scroll(object sender, UI.Xaml.Controls.Primitives.ScrollEventArgs e) {
			var viewTop = (int)e.NewValue;
			this.termContainer.UserScroll(viewTop);
		}

		private class TermControlAutomationPeer : FrameworkElementAutomationPeer {
			public TermControlAutomationPeer(UserControl owner)
				: base(owner) {
			}

			protected override bool IsContentElementCore() {
				return false;
			}

			protected override bool IsControlElementCore() {
				return false;
			}
		}
	}
}
