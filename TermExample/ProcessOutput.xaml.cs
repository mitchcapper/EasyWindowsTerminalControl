using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EasyWindowsTerminalControl;
using Microsoft.Terminal.Wpf;
using Microsoft.Win32;

namespace TermExample {
	/// <summary>
	/// Interaction logic for ProcessOutput.xaml
	/// </summary>
	public partial class ProcessOutput : Window {
		/*
		This demo uses the highlight syntax highlight generator to highlight files and put to the console.   You need highlight version 4.6 or greater which has been released! see http://andre-simon.de/zip/download.php download and unzip the x64 version somewhere and set highlight path to it
		*/
		public ProcessOutput() {
			DataContext = new DataBinds();
			InitializeComponent();
			basicTermControl.ConPTYTerm = new ReadDelimitedTermPTY(delimiter:USE_DELIMITER);
		}
		public const string HIGHLIGHT_PATH = @"highlight.exe";
		public class DataBinds : INotifyPropertyChanged {
			public void TriggerPropChanged(string prop) => PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(prop));
			
			public string StartupCommand => $"{HIGHLIGHT_PATH} --force -O truecolor --style=moria --service-mode --disable-echo --wrap";
			private static uint ColorToVal(Color color) => BitConverter.ToUInt32(new byte[] { color.R, color.G, color.B, color.A }, 0);
			private static readonly Color BackroundColor = Colors.DarkBlue;

			public event PropertyChangedEventHandler PropertyChanged;

			public SolidColorBrush BackroundColorBrush => new(BackroundColor);
			public TerminalTheme Theme { get; set; } = new() {
				DefaultBackground = ColorToVal(BackroundColor),
				DefaultForeground = ColorToVal(Colors.LightYellow),
				DefaultSelectionBackground = 0xcccccc,
				//SelectionBackgroundAlpha = 0.5f,
				CursorStyle = CursorStyle.BlinkingBar,
				ColorTable = new uint[] { 0x0C0C0C, 0x1F0FC5, 0x0EA113, 0x009CC1, 0xDA3700, 0x981788, 0xDD963A, 0xCCCCCC, 0x767676, 0x5648E7, 0x0CC616, 0xA5F1F9, 0xFF783B, 0x9E00B4, 0xD6D661, 0xF2F2F2 },
			};
		}


		private const string USE_DELIMITER = "__NOTINMYFILES__";
		private const char FILE_SEPARATOR = '\a';
		private void SelectFileClicked(object sender, RoutedEventArgs e) {
			var picker = new OpenFileDialog();
			picker.Title = "File to Highlight";
			picker.CheckFileExists = true;
			if (picker.ShowDialog() != true)
				return;

			DoFile(picker.FileName);

		}
		private DateTime startTime;
		private async void DoFile(string fileName, bool clear=true) {
			var txt = File.ReadAllText(fileName);
			if (clear) {
				basicTermControl.ConPTYTerm.ClearUITerminal(true);
				await Task.Delay(500);
			}

			var writeStr = $"syntax={Path.GetFileName(fileName)};tag={USE_DELIMITER};line-length={basicTermControl.Terminal.Columns};eof={FILE_SEPARATOR}\n{txt}\n{FILE_SEPARATOR}\n";
			writeStr = writeStr.Replace("\r", "").Replace("\n", "\r");
			startTime = DateTime.Now;
			basicTermControl.ConPTYTerm.WriteToTerm(writeStr);
			await Task.Delay(100);
			basicTermControl.ConPTYTerm.SetCursorVisibility(false);

		}

		private void CloseSTDINClicked(object sender, RoutedEventArgs e) {
			basicTermControl.ConPTYTerm.CloseStdinToApp();
		}
		private void ClearConsoleHard(object sender, RoutedEventArgs e) {
			basicTermControl.ConPTYTerm.ClearUITerminal(true);
        }

		private void ClearConsoleSoft(object sender, RoutedEventArgs e) {
			basicTermControl.ConPTYTerm.ClearUITerminal(false);
		}
	}
}
