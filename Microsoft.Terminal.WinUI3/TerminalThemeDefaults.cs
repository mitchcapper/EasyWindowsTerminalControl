using Microsoft.Terminal.Wpf;

namespace Microsoft.Terminal.WinUI3 {
	internal static class TerminalThemeDefaults {
		internal const string FontFamily = "Consolas";
		internal const short FontSize = 14;

		internal static TerminalTheme Create() => new() {
			// COLORREF is BGR. These are the Windows Terminal Campbell defaults.
			DefaultBackground = 0x000C0C0C,
			DefaultForeground = 0x00F2F2F2,
			DefaultSelectionBackground = 0x00636363,
			CursorStyle = CursorStyle.BlinkingBar,
			ColorTable = new uint[] {
				0x000C0C0C, 0x001F0FC5, 0x000EA113, 0x00009CC1,
				0x00DA3700, 0x00981788, 0x00DD963A, 0x00CCCCCC,
				0x00767676, 0x005648E7, 0x000CC616, 0x00A5F1F9,
				0x00FF783B, 0x009E00B4, 0x00D6D661, 0x00F2F2F2,
			},
		};
	}
}
