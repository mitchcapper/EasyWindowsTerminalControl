using Microsoft.Terminal.Wpf;
using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using Windows.Win32;
using System.Threading.Tasks;
using System.Text;
using System.Text.RegularExpressions;
using EasyWindowsTerminalControl.Internals;

namespace EasyWindowsTerminalControl {
	/// <summary>
	/// Class for managing communication with the underlying console, and communicating with its pseudoconsole.
	/// </summary>
	public class TermPTY : ITerminalConnection {
		protected class InternalProcessFactory : IProcessFactory {
			public IProcess Start(string command, nuint attributes, PseudoConsole console) => ProcessFactory.Start(command, attributes, console);
		}
#if WPF
		private static bool IsDesignMode = System.ComponentModel.DesignerProperties.GetIsInDesignMode(new System.Windows.DependencyObject());
#else
		private static bool IsDesignMode = false; // no designer no need to detect:)
#endif
		private SafeFileHandle _consoleInputPipeWriteHandle;
		private StreamWriter _consoleInputWriter;
		private BinaryWriter _consoleInputWriterB;
		public TermPTY(int READ_BUFFER_SIZE = 1024 * 16, bool USE_BINARY_WRITER = false, IProcessFactory ProcessFactory = null) {
			this.READ_BUFFER_SIZE = READ_BUFFER_SIZE;
			this.USE_BINARY_WRITER = USE_BINARY_WRITER;
		}
		private bool USE_BINARY_WRITER;

		public StringBuilder ConsoleOutputLog { get; private set; }
		private static Regex NewlineReduce = new(@"\n\s*?\n\s*?[\s]+", RegexOptions.Singleline);
		public static Regex colorStrip = new(@"((\x1B\[\??[0-9;]*[a-zA-Z])|\uFEFF|\u200B|\x1B\]0;|[\a\b])", RegexOptions.Compiled | RegexOptions.ExplicitCapture); //also strips BOM, bells, backspaces etc
		public string GetConsoleText(bool stripVTCodes = true) => NewlineReduce.Replace((stripVTCodes ? StripColors(ConsoleOutputLog.ToString()) : ConsoleOutputLog.ToString()).Replace("\r", ""), "\n\n").Trim();

		public static string StripColors(String str) {
			return colorStrip.Replace(str, "");
		}

		/// <summary>
		/// A stream of VT-100-enabled output from the console.
		/// </summary>
		public FileStream ConsoleOutStream { get; private set; }

		/// <summary>
		/// Fired once the console has been hooked up and is ready to receive input.
		/// </summary>
		public event EventHandler TermReady;
		public event EventHandler<TerminalOutputEventArgs> TerminalOutput;//how we send data to the UI terminal
		public bool TermProcIsStarted { get; private set; }


		/// <summary>
		/// Start the pseudoconsole and run the process as shown in 
		/// https://docs.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session#creating-the-pseudoconsole
		/// </summary>
		/// <param name="command">the command to run, e.g. cmd.exe</param>
		/// <param name="consoleHeight">The height (in characters) to start the pseudoconsole with. Defaults to 80.</param>
		/// <param name="consoleWidth">The width (in characters) to start the pseudoconsole with. Defaults to 30.</param>
		/// <param name="logOutput">Whether to log the output of the console to a file. Defaults to false.</param>
		/// <param name="factory">While not recommended, you can provide your own process factory for more granular control over process creation</param>
		public void Start(string command, int consoleWidth = 80, int consoleHeight = 30, bool logOutput = false, IProcessFactory factory = null) {
			if (Process != null)
				throw new Exception("Called Start on ConPTY term after already started");
			factory ??= new InternalProcessFactory();
			if (IsDesignMode) {
				TermProcIsStarted = true;
				TermReady?.Invoke(this, EventArgs.Empty);
				return;
			}
			if (logOutput)
				ConsoleOutputLog = new();
			using (var inputPipe = new PseudoConsolePipe())
			using (var outputPipe = new PseudoConsolePipe())
			using (var pseudoConsole = PseudoConsole.Create(inputPipe.ReadSide, outputPipe.WriteSide, consoleWidth, consoleHeight))
			using (var process = factory.Start(command, PInvoke.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, pseudoConsole)) {
				Process = process;
				TheConsole = pseudoConsole;
				// copy all pseudoconsole output to a FileStream and expose it to the rest of the app
				ConsoleOutStream = new FileStream(outputPipe.ReadSide, FileAccess.Read);
				TermProcIsStarted = true;

				TermReady?.Invoke(this, EventArgs.Empty);

				// Store input pipe handle, and a writer for later reuse
				_consoleInputPipeWriteHandle = inputPipe.WriteSide;
				var st = new FileStream(_consoleInputPipeWriteHandle, FileAccess.Write);
				if (!USE_BINARY_WRITER)
					_consoleInputWriter = new StreamWriter(st) { AutoFlush = true };
				else
					_consoleInputWriterB = new BinaryWriter(st);
				// free resources in case the console is ungracefully closed (e.g. by the 'x' in the window titlebar)
				ReadOutputLoop();
				OnClose(() => DisposeResources(process, pseudoConsole, outputPipe, inputPipe, _consoleInputWriter));

				process.WaitForExit();
				WriteToUITerminal("Session Terminated");

				TheConsole.Dispose();
			}
		}
		public IProcess Process { get; protected set; }
		PseudoConsole TheConsole;
		/// <summary>
		/// Sends the given string to the anonymous pipe that writes to the active pseudoconsole.
		/// </summary>
		/// <param name="input">A string of characters to write to the console. Supports VT-100 codes.</param>
		public void WriteToTerm(ReadOnlySpan<char> input) {
			if (IsDesignMode)
				return;
			if (TheConsole.IsDisposed)
				return;
			if (_consoleInputWriter == null && _consoleInputWriterB == null)
				throw new InvalidOperationException("There is no writer attached to a pseudoconsole. Have you called Start on this instance yet?");
			if (!USE_BINARY_WRITER)
				_consoleInputWriter.Write(input);
			else
				WriteToTermBinary(Encoding.UTF8.GetBytes(input.ToString()));
		}
		public void WriteToTermBinary(ReadOnlySpan<byte> input) {
			if (!USE_BINARY_WRITER) {
				WriteToTerm(Encoding.UTF8.GetString(input));
				return;
			}
			_consoleInputWriterB.Write(input);
			_consoleInputWriterB.Flush();
		}
		/// <summary>
		/// Close the input stream to the process (will send EOF if attempted to be read). 
		/// </summary>
		public void CloseStdinToApp() {
			_consoleInputWriter?.Close();
			_consoleInputWriter?.Dispose();
			_consoleInputWriterB?.Close();
			_consoleInputWriterB?.Dispose();
			_consoleInputWriter = null;
			_consoleInputWriterB = null;
		}
		public void StopExternalTermOnly() {
			if (Process?.HasExited != false) return;
			Process.Kill();
		}
		/// <summary>
		/// Set a callback for when the terminal is closed (e.g. via the "X" window decoration button).
		/// Intended for resource cleanup logic.
		/// </summary>
		private static void OnClose(Action handler) {
			PInvoke.SetConsoleCtrlHandler(eventType => {
				if (eventType == PInvoke.CTRL_CLOSE_EVENT) {
					handler();
				}
				return false;
			}, true);
		}

		private void DisposeResources(params IDisposable[] disposables) {
			foreach (var disposable in disposables) {
				disposable.Dispose();
			}
		}

		public void Start() {
			if (IsDesignMode) {
				WriteToUITerminal("MyShell DesignMode:> Your command window content here\r\n");
				return;
			}

			Task.Run(ReadOutputLoop);
		}

		/// <summary>
		/// Note if you change the span to a 0 length then no input will be passed on
		/// </summary>
		/// <param name="str"></param>
		public delegate void InterceptDelegate(ref Span<char> str);

		public InterceptDelegate InterceptOutputToUITerminal;
		public InterceptDelegate InterceptInputToTermApp;
		/// <summary>
		/// This simulates output from the program itself to the terminal window, ANSI sequences can be sent here as well
		/// </summary>
		/// <param name="str"></param>
		public void WriteToUITerminal(ReadOnlySpan<char> str) {
			//Debug.WriteLine($"Term.cs WriteToUITerminal got: {str.ToString().Replace("\n","\n\t").Trim()}");
			TerminalOutput?.Invoke(this, new TerminalOutputEventArgs(str.ToString()));
		}
		public bool ReadLoopStarted = false;

		/// <summary>
		/// Sets if the GUI Terminal control communicates to ConPTY using extended key events (handles certain control sequences better)
		/// https://github.com/microsoft/terminal/blob/main/doc/specs/%234999%20-%20Improved%20keyboard%20handling%20in%20Conpty.md
		/// </summary>
		/// <param name="enable"></param>
		public void Win32DirectInputMode(bool enable) {
			var decSet = enable ? "h" : "l";
			var str = $"\x1b[?9001{decSet}";
			WriteToUITerminal(str);
		}
		int READ_BUFFER_SIZE;
		protected ref struct ReadState {
			public Span<char> entireBuffer;
			public Span<char> curBuffer;
			public int readChars;
		}
		protected virtual void ReadOutputLoop() {
			if (ReadLoopStarted)
				return;
			ReadLoopStarted = true;
			// We have a few ways to handle the buffer with a delimiter but given the size of the buffer and the fairly cheap cost of copying, the ability to let the span be modified before passing it on, we will just copy any parts before the next delimiter to the start of the buffer when reaching the end.
			using (StreamReader reader = new StreamReader(ConsoleOutStream)) {
				ReadState state = new() { entireBuffer = new char[READ_BUFFER_SIZE] };

				state.curBuffer = state.entireBuffer.Slice(0);

				while ((state.readChars = reader.Read(state.curBuffer)) != 0) {
					//					Debug.WriteLine($"Read: {read}");

					var sendSpan = HandleRead(ref state);

					if (!sendSpan.IsEmpty) {
						InterceptOutputToUITerminal?.Invoke(ref sendSpan);
						if (sendSpan.Length > 0) {
							var str = sendSpan.ToString();
							WriteToUITerminal(str);
							ConsoleOutputLog?.Append(str);
						}
					}
				}
			}
		}
		/// <summary>
		/// return the span (if any) to send to client, and the curBuffer
		/// </summary>
		/// <param name="curBuffer"></param>
		/// <returns></returns>
		protected virtual Span<char> HandleRead(ref ReadState state) {
			return state.curBuffer.Slice(0, state.readChars);
		}


		/// <summary>
		/// When set to true and input from the UI WPF Terminal Control will be ignored, will still invoke the intercept event.  You can still call WriteToTerm to write to the app yourself.
		/// </summary>
		/// <param name="readOnly">Enable / Disable readonly mode</param>
		/// <param name="updateCursor">Will hide/show the cursor depending on readonly setting</param>
		public void SetReadOnly(bool readOnly = true, bool updateCursor = true) {
			_ReadOnly = readOnly;
			if (updateCursor)
				SetCursorVisibility(!readOnly);
		}
		protected bool _ReadOnly;

		void ITerminalConnection.WriteInput(string data) {
			Span<char> span = data.ToCharArray();
			InterceptInputToTermApp?.Invoke(ref span);
			if (span.Length > 0 && !_ReadOnly)
				WriteToTerm(span);
		}

		void ITerminalConnection.Resize(uint row_height, uint column_width) {
			TheConsole?.Resize((int)column_width, (int)row_height);
		}
		public void Resize(int column_width, int row_height) {
			TheConsole?.Resize(column_width, row_height);
		}
		public void SetCursorVisibility(bool visible) => WriteToUITerminal("\x1b[?25" + (visible ? 'h' : 'l'));
		/// <summary>
		/// 
		/// </summary>
		/// <param name="fullReset">Means near all parameters of the term are reset to defaults rather than just clearing the screen</param>
		public void ClearUITerminal(bool fullReset = false) => WriteToUITerminal(fullReset ? "\x001bc\x1b]104\x1b\\" : "\x1b[H\x1b[2J\u001b[3J");

		void ITerminalConnection.Close() {
			TheConsole?.Dispose();
		}
	}
}
