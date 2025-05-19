using System;
using System.ComponentModel;
using System.Threading.Tasks;

using EasyWindowsTerminalControl;
using Microsoft.Terminal.Wpf;



#if WPF
using System.Windows.Media;
using System.Windows;
using System.Windows.Input;
#else
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using WinUIEx;
using Key = Windows.System.VirtualKey;
using Color = System.Drawing.Color;
using Colors = System.Drawing.Color;
using Microsoft.UI.Xaml.Controls;
#endif
#if WPF
namespace TermExample {
#else
namespace TermExample.WinUI {
	internal static class Keyboard {
		public static void Focus(this UIElement elem) => elem.Focus(FocusState.Programmatic);
		public static bool IsKeyDown(VirtualKey key) => Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
	}
#endif
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window {
		/// <summary>
		/// when mirror mode is on the duplicate new button still shows the terminal attached to the old window too
		/// </summary>
		private const bool MirrorMode = false;
		public MainWindow() {

			InitializeComponent();
#if WPF
			DataContext = binds;
#endif
		}
		DataBinds binds { get; set; } = new();
		public MainWindow(TermPTY existingTerm) {
			InitializeComponent();
#if WPF
			DataContext = binds;
#endif
			basicTermControl.DisconnectConPTYTerm();//This should be used but only after the TerminalContainer patch is applied
			basicTermControl.ConPTYTerm = existingTerm;
		}

		public class DataBinds : INotifyPropertyChanged {
			public void TriggerPropChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));

			public string StartupCommand => "pwsh.exe";
			private static Color AlphaOverrideColor(Color color, byte alphaOverride) => Color.FromArgb(alphaOverride, color.R, color.G, color.B);

			private static readonly Color BackroundColor = Color.FromArgb(255, 0, 0, 30);
			private static Windows.UI.Color BackroundColorUI => Windows.UI.Color.FromArgb(BackroundColor.A, BackroundColor.R, BackroundColor.G, BackroundColor.B);

			public event PropertyChangedEventHandler PropertyChanged;

			public SolidColorBrush BackroundColorBrush => new(
#if WPF
				BackroundColor
#else
				BackroundColorUI
#endif
				);
			//private TerminalTheme _Theme;
			public TerminalTheme Theme { get; set; } = new() {
				DefaultBackground = EasyTerminalControl.ColorToVal(BackroundColor),
				DefaultForeground = EasyTerminalControl.ColorToVal(Colors.LightYellow),
				DefaultSelectionBackground = 0xcccccc,
				//SelectionBackgroundAlpha = 0.5f, 
				CursorStyle = CursorStyle.BlinkingBar,
				ColorTable = new uint[] { 0x0C0C0C, 0x1F0FC5, 0x0EA113, 0x009CC1, 0xDA3700, 0x981788, 0xDD963A, 0xCCCCCC, 0x767676, 0x5648E7, 0x0CC616, 0xA5F1F9, 0xFF783B, 0x9E00B4, 0xD6D661, 0xF2F2F2 },
			};

		}
		private async void RefocusKB() {
			await Task.Delay(50);
			basicTermControl.Focus();
			Keyboard.Focus(basicTermControl);
		}
		private void ShowBufferClicked(object sender, RoutedEventArgs e) {
			var msg = basicTermControl.ConPTYTerm.GetConsoleText();
			if (Keyboard.IsKeyDown(Key.LeftShift))
				basicTermControl.ConPTYTerm.ConsoleOutputLog.Clear();
			else
				MessageBoxShow(msg);
			
			RefocusKB();
		}


		private void ClearTermClicked(object sender, RoutedEventArgs e) {
			basicTermControl.ConPTYTerm.ClearUITerminal();
			RefocusKB();

		}
		private void RestartTermClicked(object sender, RoutedEventArgs e) {
			basicTermControl.RestartTerm();
			RefocusKB();

		}


		private void DuplicateClicked(object sender, RoutedEventArgs e) {
			// Don't really recommend doing this basic cloning we will sync our size at least so the positionings are correct.
			var wind = new MainWindow(basicTermControl.ConPTYTerm);
			if (MirrorMode)
				wind.SizeChanged += (sender, _) => MirrorSizeChanged(sender);
			else
				basicTermControl.DisconnectConPTYTerm();
			wind.Show();
		}

		private void MirrorSizeChanged(object sender) {
			var wind = sender as MainWindow;

#if WPF
			Width = wind.Width;
			Height = wind.Height;

#else
			this.AppWindow.Resize(wind.AppWindow.Size);
#endif

		}

#if WPF
		private void ShowProcessOutputClicked(object sender, RoutedEventArgs e) {
			var wind = new ProcessOutput();
			wind.Show();
		}
		private void MessageBoxShow(string msg) => MessageBox.Show(msg);
#else
		
		private async void MessageBoxShow(string msg) {
			var dialog = new ContentDialog {
				Content = new TextBlock { Text = msg.Replace("\n","\r\n"), TextWrapping = TextWrapping.Wrap },
				CloseButtonText = "OK"
				
			};
			dialog.XamlRoot = gridMain.XamlRoot;
			basicTermControl.Visibility = Visibility.Collapsed;
			await dialog.ShowAsync();
			basicTermControl.Visibility = Visibility.Visible;
		}

#endif
	}

}
