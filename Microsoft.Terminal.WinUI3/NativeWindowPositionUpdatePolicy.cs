namespace System.Windows.Interop {
	internal static class NativeWindowPositionUpdatePolicy {
		internal static bool ShouldQueue(
			bool isDisposed,
			bool isApplyingWindowPosition,
			bool hasValidSize,
			bool matchesLastXamlSize) =>
			!isDisposed &&
			!isApplyingWindowPosition &&
			hasValidSize &&
			!matchesLastXamlSize;
	}
}
