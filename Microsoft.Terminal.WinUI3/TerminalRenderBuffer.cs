using System;
using System.Text;

namespace Microsoft.Terminal.WinUI3 {
	/// <summary>
	/// Retains the renderer input stream so a native terminal HWND can be rebuilt
	/// after a tab reattachment without losing its visible scrollback.
	/// </summary>
	internal sealed class TerminalRenderBuffer {
		private const int MaximumCharacters = 2_000_000;
		private const string FullReset = "\x001bc";
		private const string ClearScrollback = "\x1b[3J";
		private readonly StringBuilder _content = new();

		internal void Append(string data) {
			if (string.IsNullOrEmpty(data)) {
				return;
			}

			var resetIndex = Math.Max(
				data.LastIndexOf(FullReset, StringComparison.Ordinal),
				data.LastIndexOf(ClearScrollback, StringComparison.Ordinal));
			if (resetIndex >= 0) {
				_content.Clear();
				_content.Append(data, resetIndex, data.Length - resetIndex);
			} else {
				_content.Append(data);
			}

			if (_content.Length > MaximumCharacters) {
				_content.Remove(0, _content.Length - MaximumCharacters);
			}
		}

		internal string Snapshot() => _content.ToString();
	}
}
