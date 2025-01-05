using System;
using System.ComponentModel;
using System.Threading.Tasks;
#if WPF
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
#else
using System.Drawing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Terminal.WinUI3;
using FontFamily = Microsoft.UI.Xaml.Media.FontFamily;
using Microsoft.UI.Xaml.Input;
#endif
using EasyWindowsTerminalControl.Internals;
using Microsoft.Terminal.Wpf;

using System.Diagnostics;


namespace EasyWindowsTerminalControl {
	public class EasyTerminalControl : UserControl {
		/// <summary>
		/// Converts Color to COLOREF, note that COLOREF does not support alpha channels so it is ignored
		/// </summary>
		/// <param name="color"></param>
		/// <returns></returns>
		public static uint ColorToVal(Color color) => BitConverter.ToUInt32(new byte[] { color.R, color.G, color.B, 0 }, 0);
		public EasyTerminalControl() {
			InitializeComponent();
			SetKBCaptureOptions();
#if WPF
#else
			this.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, OnVisibleChanged);
#endif
		}
#if WPF
#else
		private void OnVisibleChanged(DependencyObject sender, DependencyProperty dp) {
			if (Terminal != null)
				Terminal.Visibility = Visibility;
		}
#endif
		[Flags]
		[System.ComponentModel.TypeConverter(typeof(System.ComponentModel.EnumConverter))]
		public enum INPUT_CAPTURE { None = 1 << 0, TabKey = 1 << 1, DirectionKeys = 1 << 2 };



		private static void InputCaptureChanged(DependencyObject target, DependencyPropertyChangedEventArgs e) {
			var cntrl = target as EasyTerminalControl;
			cntrl.SetKBCaptureOptions();
		}
		private void SetKBCaptureOptions() {
#if WPF
			KeyboardNavigation.SetTabNavigation(this, InputCapture.HasFlag(INPUT_CAPTURE.TabKey) ? KeyboardNavigationMode.Contained : KeyboardNavigationMode.Continue);
			KeyboardNavigation.SetDirectionalNavigation(this, InputCapture.HasFlag(INPUT_CAPTURE.DirectionKeys) ? KeyboardNavigationMode.Contained : KeyboardNavigationMode.Continue);
#endif
		}
		/// <summary>
		/// Helper property for setting KeyboardNavigation.Set*Navigation commands to prevent arrow keys or tabs from causing us to leave the control (aka pass through to conpty)
		/// </summary>
		public INPUT_CAPTURE InputCapture {
			get => (INPUT_CAPTURE)GetValue(InputCaptureProperty);
			set => SetValue(InputCaptureProperty, value);
		}

		[Description("Write only, sets the terminal theme"), Category("Common")]
		public TerminalTheme? Theme { set => SetTheme(_Theme = value); private get => _Theme; }
		private TerminalTheme? _Theme;
		private void SetTheme(TerminalTheme? v) { if (v != null) Terminal?.SetTheme(v.Value, FontFamilyWhenSettingTheme.Source, (short)FontSizeWhenSettingTheme); }



		[Description("Write only, When true user cannot give input through the Terminal UI (can still write to the Term from code behind using Term.WriteToTerm)"), Category("Common")]
		public bool? IsReadOnly { set => SetReadOnly(_IsReadOnly = value); private get => _IsReadOnly; }
		private bool? _IsReadOnly;
		private void SetReadOnly(bool? v) { if (v != null) ConPTYTerm?.SetReadOnly(v.Value, false); }//no cursor auto update if user wants that they can use the separate dependency property for the cursor visibility

		[Description("Write only, if the type cursor shows on the Terminal UI"), Category("Common")]
		public bool? IsCursorVisible { set => SetCursor(_IsCursorVisible = value); private get => _IsCursorVisible; }
		private bool? _IsCursorVisible;
		private void SetCursor(bool? v) { if (v != null) ConPTYTerm?.SetCursorVisibility(v.Value); }

		[Description("Direct access to the UI terminal control itself that handles rendering")]
		public TerminalControl Terminal {
#if WPF
			get => (TerminalControl)GetValue(TerminalPropertyKey.DependencyProperty);
			set => SetValue(TerminalPropertyKey, value);
#else
			get => (TerminalControl)GetValue(TerminalProperty);
			set => SetValue(TerminalProperty, value);
#endif
		}

		private static void OnTermChanged(DependencyObject target, DependencyPropertyChangedEventArgs e) {
			var cntrl = (target as EasyTerminalControl);
			var newTerm = e.NewValue as TermPTY;
			if (newTerm != null) {
				if (cntrl.Terminal.IsLoaded)
					cntrl.Terminal_Loaded(cntrl.Terminal, null);

				if (newTerm.TermProcIsStarted)
					cntrl.Term_TermReady(newTerm, null);
				else
					newTerm.TermReady += cntrl.Term_TermReady;
			}
		}
		/// <summary>
		/// Update the Term if you want to set to an existing
		/// </summary>
		[Description("The backend TermPTY connection allows changing the application the control is connected to")]
		public TermPTY ConPTYTerm {
			get => (TermPTY)GetValue(ConPTYTermProperty);
			set => SetValue(ConPTYTermProperty, value);
		}


		public TermPTY DisconnectConPTYTerm() {
			if (Terminal != null)
				Terminal.Connection = null;
			if (ConPTYTerm != null)
				ConPTYTerm.TermReady -= Term_TermReady;
			var ret = ConPTYTerm;
			ConPTYTerm = null;
			return ret;
		}

		public string StartupCommandLine {
			get => (string)GetValue(StartupCommandLineProperty);
			set => SetValue(StartupCommandLineProperty, value);
		}

		public bool LogConPTYOutput {
			get => (bool)GetValue(LogConPTYOutputProperty);
			set => SetValue(LogConPTYOutputProperty, value);
		}
		/// <summary>
		/// Sets if the GUI Terminal control communicates to ConPTY using extended key events (handles certain control sequences better)
		/// https://github.com/microsoft/terminal/blob/main/doc/specs/%234999%20-%20Improved%20keyboard%20handling%20in%20Conpty.md
		/// </summary>
		public bool Win32InputMode {
			get => (bool)GetValue(Win32InputModeProperty);
			set => SetValue(Win32InputModeProperty, value);
		}

		public FontFamily FontFamilyWhenSettingTheme {
			get => (FontFamily)GetValue(FontFamilyWhenSettingThemeProperty);
			set => SetValue(FontFamilyWhenSettingThemeProperty, value);
		}

		public int FontSizeWhenSettingTheme {
			get => (int)GetValue(FontSizeWhenSettingThemeProperty);
			set => SetValue(FontSizeWhenSettingThemeProperty, value);
		}
		private void InitializeComponent() {
			Terminal = new();
			ConPTYTerm = new();
			Terminal.AutoResize = true;
			Terminal.Loaded += Terminal_Loaded;
			var grid = new Grid() { };
			grid.Children.Add(Terminal);
			this.Content = grid;
#if WPF
			Focusable = true;
			Terminal.Focusable = true;
			this.GotFocus += (_, _) => Terminal.Focus();
#else
			this.GotFocus += (_, _) => Terminal.Focus(FocusState.Pointer);
			IsTabStop = true;
			//FocusManager.GotFocus += (o,e) => Debug.WriteLine($"FocusManager: {e.NewFocusedElement}");
#endif

		}

#if WPF
		void MainThreadRun(Action action) => Dispatcher.Invoke(action);
#else
		async void MainThreadRun(Action action) => await UWPHelpers.Enqueue(this.DispatcherQueue, action);
#endif

		private void Term_TermReady(object sender, EventArgs e) {
			MainThreadRun(() => {
				Terminal.Connection = ConPTYTerm;
				ConPTYTerm.Win32DirectInputMode(Win32InputMode);
				ConPTYTerm.Resize(Terminal.Columns, Terminal.Rows);//fix the size being partially off on first load
			});
		}
		private void StartTerm(int column_width, int row_height) {
			if (ConPTYTerm == null)
				return;

			if (ConPTYTerm.TermProcIsStarted) {
				ConPTYTerm.Resize(column_width, row_height);
				Term_TermReady(ConPTYTerm, null);
				return;
			}
			ConPTYTerm.TermReady += Term_TermReady;
			MainThreadRun(() => {
				var cmd = StartupCommandLine;//thread safety for dp
				var term = ConPTYTerm;
				var logOutput = LogConPTYOutput;
				Task.Run(() => term.Start(cmd, column_width, row_height, logOutput));
			});
		}
		private async void Terminal_Loaded(object sender, RoutedEventArgs e) {
			StartTerm(Terminal.Columns, Terminal.Rows);
			SetTheme(Theme);
			SetCursor(IsCursorVisible);
			SetReadOnly(IsReadOnly);
			//Terminal.Focus();
			await Task.Delay(1000);
			SetCursor(IsCursorVisible);
		}

		#region Depdendency Properties
		public static readonly DependencyProperty InputCaptureProperty = DependencyProperty.Register(nameof(InputCapture), typeof(INPUT_CAPTURE), typeof(EasyTerminalControl), new
		PropertyMetadata(INPUT_CAPTURE.TabKey | INPUT_CAPTURE.DirectionKeys, InputCaptureChanged));

		public static readonly DependencyProperty ThemeProperty = PropHelper.GenerateWriteOnlyProperty((c) => c.Theme);
#if WPF
		protected static readonly DependencyPropertyKey TerminalPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Terminal), typeof(TerminalControl), typeof(EasyTerminalControl), new PropertyMetadata());
		public static readonly DependencyProperty TerminalProperty = TerminalPropertyKey.DependencyProperty;
#else
		public static readonly DependencyProperty TerminalProperty = DependencyProperty.Register(nameof(Terminal), typeof(TerminalControl), typeof(EasyTerminalControl), new PropertyMetadata(null));
#endif
		public static readonly DependencyProperty ConPTYTermProperty = DependencyProperty.Register(nameof(ConPTYTerm), typeof(TermPTY), typeof(EasyTerminalControl), new(null, OnTermChanged));
		public static readonly DependencyProperty StartupCommandLineProperty = DependencyProperty.Register(nameof(StartupCommandLine), typeof(string), typeof(EasyTerminalControl), new PropertyMetadata("powershell.exe"));

		public static readonly DependencyProperty LogConPTYOutputProperty = DependencyProperty.Register(nameof(LogConPTYOutput), typeof(bool), typeof(EasyTerminalControl), new PropertyMetadata(false));
		public static readonly DependencyProperty Win32InputModeProperty = DependencyProperty.Register(nameof(Win32InputMode), typeof(bool), typeof(EasyTerminalControl), new PropertyMetadata(true));
		public static readonly DependencyProperty IsReadOnlyProperty = PropHelper.GenerateWriteOnlyProperty((c) => c.IsReadOnly);
		public static readonly DependencyProperty IsCursorVisibleProperty = PropHelper.GenerateWriteOnlyProperty((c) => c.IsCursorVisible);

		public static readonly DependencyProperty FontFamilyWhenSettingThemeProperty = DependencyProperty.Register(nameof(FontFamilyWhenSettingTheme), typeof(FontFamily), typeof(EasyTerminalControl), new PropertyMetadata(new FontFamily("Cascadia Code")));

		public static readonly DependencyProperty FontSizeWhenSettingThemeProperty = DependencyProperty.Register(nameof(FontSizeWhenSettingTheme), typeof(int), typeof(EasyTerminalControl), new PropertyMetadata(12));

		private class PropHelper : DepPropHelper<EasyTerminalControl> { }

		#endregion
	}
}
