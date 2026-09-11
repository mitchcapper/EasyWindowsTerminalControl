#nullable enable

using System;
using Microsoft.Terminal.Wpf;

namespace Microsoft.Terminal.WinUI3 {
	/// <summary>
	/// Applies backend changes to a terminal renderer. An identical backend is a
	/// no-op, retaining scrollback and avoiding a second Start call on tab
	/// reattachment.
	/// </summary>
	internal sealed class TerminalConnectionLifecycle {
		private readonly TerminalConnectionBinding<ITerminalConnection> _binding = new();
		private readonly EventHandler<TerminalOutputEventArgs> _renderOutput;

		internal TerminalConnectionLifecycle(EventHandler<TerminalOutputEventArgs> renderOutput) {
			_renderOutput = renderOutput ?? throw new ArgumentNullException(nameof(renderOutput));
		}

		internal ITerminalConnection? Current => _binding.Current;

		internal bool SetConnection(ITerminalConnection? next) {
			var transition = _binding.Replace(next);
			if (!transition.Changed) {
				return false;
			}

			if (transition.Previous != null) {
				transition.Previous.TerminalOutput -= _renderOutput;
			}

			_renderOutput(this, new TerminalOutputEventArgs("\x001bc\x1b]104\x1b\\"));
			if (transition.Current != null) {
				if (transition.Previous == null) {
					_renderOutput(this, new TerminalOutputEventArgs("\x1b[?25h"));
				}

				transition.Current.TerminalOutput += _renderOutput;
				transition.Current.Start();
			} else {
				_renderOutput(this, new TerminalOutputEventArgs("\x1b[?25l"));
			}

			return true;
		}
	}
}
