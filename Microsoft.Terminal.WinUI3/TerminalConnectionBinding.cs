#nullable enable

namespace Microsoft.Terminal.WinUI3 {
	/// <summary>
	/// Tracks the terminal backend bound to a native renderer. Rebinding exactly
	/// the same backend is intentionally a no-op: the renderer keeps its buffer
	/// and the backend is not started again when a tab is reattached.
	/// </summary>
	internal sealed class TerminalConnectionBinding<TConnection>
		where TConnection : class {
		internal TConnection? Current { get; private set; }

		internal TerminalConnectionTransition<TConnection> Replace(TConnection? next) {
			if (ReferenceEquals(Current, next)) {
				return TerminalConnectionTransition<TConnection>.Unchanged;
			}

			var previous = Current;
			Current = next;
			return new TerminalConnectionTransition<TConnection>(previous, next, true);
		}
	}

	internal readonly record struct TerminalConnectionTransition<TConnection>(
		TConnection? Previous,
		TConnection? Current,
		bool Changed)
		where TConnection : class {
		internal static TerminalConnectionTransition<TConnection> Unchanged =>
			new(default, default, false);
	}
}
