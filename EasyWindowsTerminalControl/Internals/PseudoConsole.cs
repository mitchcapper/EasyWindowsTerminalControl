using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.System.Console;

namespace EasyWindowsTerminalControl.Internals {
	/// <summary>
	/// Utility functions around the new Pseudo Console APIs.
	/// </summary>
	public class PseudoConsole : IDisposable {
		private bool disposed;
		public bool IsDisposed => disposed;
		internal ConPtyClosePseudoConsoleSafeHandle Handle { get; }

		/// <summary>
		/// Required for any 3rd parties trying to implement their own process creation
		/// </summary>
		public IntPtr GetDangerousHandle => Handle.DangerousGetHandle();

		private PseudoConsole(ConPtyClosePseudoConsoleSafeHandle handle) {
			Handle = handle;
		}
		public void Resize(int width, int height) {
			PseudoConsoleApi.ResizePseudoConsole(Handle.DangerousGetHandle(), new COORD { X = (short)width, Y = (short)height });
		}
		internal class ConPtyClosePseudoConsoleSafeHandle : ClosePseudoConsoleSafeHandle {
			public ConPtyClosePseudoConsoleSafeHandle(IntPtr preexistingHandle, bool ownsHandle = true) : base(preexistingHandle, ownsHandle) {
			}
			protected override bool ReleaseHandle() {
				PseudoConsoleApi.ClosePseudoConsole(handle);
				return true;
			}
		}
		public static PseudoConsole Create(SafeFileHandle inputReadSide, SafeFileHandle outputWriteSide, int width, int height) {
			if (width == 0 || height == 0){
				Debug.WriteLine($"PseudoConsole Create called with 0 width height");
				width = 80;
				height=30;
			}
			var createResult = PseudoConsoleApi.CreatePseudoConsole(
				new COORD { X = (short)width, Y = (short)height },
				inputReadSide, outputWriteSide,
			   0, out IntPtr hPC);
			if (createResult != 0) {
				throw new Win32Exception(createResult);
				//throw new Win32Exception(createResult, "Could not create pseudo console.");
			}
			return new PseudoConsole(new ConPtyClosePseudoConsoleSafeHandle(hPC));
		}

		private void Dispose(bool disposing) {
			if (!disposed) {
				if (disposing) {
					Handle.Dispose();
				}

				// TODO: set large fields to null
				disposed = true;
			}
		}

		public void Dispose() {
			// Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}

	}
}
