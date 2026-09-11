using System;

namespace System.Windows.Interop {
	internal static class NativeWindowResizeOrder {
		internal static bool Apply(
			Func<bool> applyWindowPosition,
			Action afterWindowPositionApplied) {
			if (!applyWindowPosition()) {
				return false;
			}

			afterWindowPositionApplied();
			return true;
		}
	}
}
