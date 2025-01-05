using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Windows.Win32;
using Windows.Win32.Foundation;
namespace Microsoft.Terminal.WinUI3 {
	public static class UWPHelpers {
		internal class WindowChildEnumerator {
			protected List<HWND> all = new();
			protected BOOL Callback(HWND hWnd, LPARAM lparam) {
				all.Add(hWnd);
				return true;
			}
			public WindowChildEnumerator(HWND parent) => PInvoke.EnumChildWindows(parent, Callback, 0);

			public IEnumerable<HWND> Result => all;
		}
		internal static unsafe string GetClassName(HWND hwnd) {
			var className = stackalloc char[256];
			var count = PInvoke.GetClassName(hwnd, className, 256);
			return new string(className, 0, count);
		}

		/// <summary>
		/// Gets the window that actually handles all window messages for that Winui3 Window
		/// </summary>
		/// <param name="rootHwnd"></param>
		/// <returns></returns>
		public static IntPtr GetInputHwnd(IntPtr rootHwnd) {
			return new WindowChildEnumerator(new(rootHwnd)).Result.FirstOrDefault(win => GetClassName(win) == "InputSiteWindowClass");
		}
		public static Task Enqueue(this DispatcherQueue dispatcher, Action action, DispatcherQueuePriority priority = DispatcherQueuePriority.Normal) {
			try {
				if (dispatcher.HasThreadAccess) {
					action();
					return Task.CompletedTask;
				}
				var tcs = new TaskCompletionSource<object>();
				dispatcher.TryEnqueue(priority, () => {
					try {
						action();
						tcs.SetResult(null);
					} catch (Exception ex) {
						tcs.SetException(ex);
					}
				});
				return tcs.Task;
			} catch (Exception ex) {
				return Task.FromException(ex);
			}
		}

	}
}
