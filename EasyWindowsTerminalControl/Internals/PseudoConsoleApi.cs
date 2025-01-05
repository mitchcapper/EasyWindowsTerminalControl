using Microsoft.Win32.SafeHandles;
using System;
using System.Runtime.InteropServices;
using Windows.Win32.System.Console;
namespace EasyWindowsTerminalControl.Internals {
	/// <summary>
	/// PInvoke signatures for Win32's PseudoConsole API.
	/// </summary>
	public static class PseudoConsoleApi {

		[DllImport("conpty.dll", SetLastError = true)]
		internal static extern int CreatePseudoConsole(COORD size, SafeFileHandle hInput, SafeFileHandle hOutput, uint dwFlags, out IntPtr phPC);
		[DllImport("conpty.dll", SetLastError = true)]
		internal static extern int ClosePseudoConsole(IntPtr hPC);
		[DllImport("conpty.dll", SetLastError = true)]
		internal static extern int ResizePseudoConsole(IntPtr hPC, COORD size);



	}
}
