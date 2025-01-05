using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Interop;
using Microsoft.UI.Xaml.Automation.Peers;
using Windows.System;
using WinUIEx.Messaging;

namespace Microsoft.Terminal.WinUI3.WPFImports {
	public struct DpiScale {
		/// <summary>Initializes a new instance of the <see cref="T:System.Windows.DpiScale" /> structure.</summary>
		/// <param name="dpiScaleX">The DPI scale on the X axis.</param>
		/// <param name="dpiScaleY">The DPI scale on the Y axis.</param>
		// Token: 0x0600174F RID: 5967 RVA: 0x0048792C File Offset: 0x0048792C
		public DpiScale(double dpiScaleX, double dpiScaleY) {
			_dpiScaleX = dpiScaleX;
			_dpiScaleY = dpiScaleY;
		}

		/// <summary>Gets the DPI scale on the X axis.</summary>
		/// <returns>The DPI scale for the X axis.</returns>
		// Token: 0x1700066A RID: 1642
		// (get) Token: 0x06001750 RID: 5968 RVA: 0x0048793C File Offset: 0x0048793C
		public double DpiScaleX => _dpiScaleX;

		/// <summary>Gets the DPI scale on the Yaxis.</summary>
		/// <returns>The DPI scale for the Y axis.</returns>
		// Token: 0x1700066B RID: 1643
		// (get) Token: 0x06001751 RID: 5969 RVA: 0x00487944 File Offset: 0x00487944
		public double DpiScaleY => _dpiScaleY;

		/// <summary>Get or sets the PixelsPerDip at which the text should be rendered.</summary>
		/// <returns>The current <see cref="P:System.Windows.DpiScale.PixelsPerDip" /> value.</returns>
		// Token: 0x1700066C RID: 1644
		// (get) Token: 0x06001752 RID: 5970 RVA: 0x0048794C File Offset: 0x0048794C
		public double PixelsPerDip => _dpiScaleY;

		/// <summary>Gets the DPI along X axis.</summary>
		/// <returns>The DPI along the X axis.</returns>
		// Token: 0x1700066D RID: 1645
		// (get) Token: 0x06001753 RID: 5971 RVA: 0x00487954 File Offset: 0x00487954
		public double PixelsPerInchX => 96.0 * _dpiScaleX;

		/// <summary>Gets the DPI along Y axis.</summary>
		/// <returns>The DPI along the Y axis.</returns>
		// Token: 0x1700066E RID: 1646
		// (get) Token: 0x06001754 RID: 5972 RVA: 0x00487968 File Offset: 0x00487968
		public double PixelsPerInchY => 96.0 * _dpiScaleY;

		// Token: 0x06001755 RID: 5973 RVA: 0x0048797C File Offset: 0x0048797C
		internal bool Equals(DpiScale other) {
			if (_dpiScaleX == other._dpiScaleX) {
				return _dpiScaleY == other._dpiScaleY;
			}
			return false;
		}

		// Token: 0x04000F55 RID: 3925
		private readonly double _dpiScaleX;

		// Token: 0x04000F56 RID: 3926
		private readonly double _dpiScaleY;
	}
	public sealed class DpiChangedEventArgs : EventArgs {
		// Token: 0x06001738 RID: 5944 RVA: 0x004877EC File Offset: 0x004877EC
		public DpiChangedEventArgs(DpiScale oldDpi, DpiScale newDpi, object source)
			: base() {
			OldDpi = oldDpi;
			NewDpi = newDpi;
		}

		/// <summary>Gets the DPI scale information before a DPI change.</summary>
		/// <returns>Information about the previous DPI scale.</returns>
		// Token: 0x17000660 RID: 1632
		// (get) Token: 0x06001739 RID: 5945 RVA: 0x00487808 File Offset: 0x00487808
		// (set) Token: 0x0600173A RID: 5946 RVA: 0x00487810 File Offset: 0x00487810
		public DpiScale OldDpi { get; private set; }

		/// <summary>Gets the scale information after a DPI change.</summary>
		/// <returns>The new DPI scale information.</returns>
		// Token: 0x17000661 RID: 1633
		// (get) Token: 0x0600173B RID: 5947 RVA: 0x0048781C File Offset: 0x0048781C
		// (set) Token: 0x0600173C RID: 5948 RVA: 0x00487824 File Offset: 0x00487824
		public DpiScale NewDpi { get; private set; }
	}
	public delegate void DpiChangedEventHandler(object sender, DpiChangedEventArgs e);
	public interface IKeyboardInputSite {
		/// <summary>Unregisters a child keyboard input sink from this site. </summary>
		// Token: 0x060010B4 RID: 4276
		void Unregister();

		/// <summary>Gets the keyboard sink associated with this site. </summary>
		/// <returns>The current site's <see cref="T:System.Windows.Interop.IKeyboardInputSink" /> interface.</returns>
		// Token: 0x170004E0 RID: 1248
		// (get) Token: 0x060010B5 RID: 4277
		IKeyboardInputSink Sink { get; }

		/// <summary>Called by a contained component when it has reached its last tab stop and has no further items to tab to. </summary>
		/// <param name="request">Specifies whether focus should be set to the first or the last tab stop.</param>
		/// <returns>If this method returns <see langword="true" />, the site has shifted focus to another component. If this method returns <see langword="false" />, focus is still within the calling component. The component should "wrap around" and set focus to its first contained tab stop.</returns>
		// Token: 0x060010B6 RID: 4278
		bool OnNoMoreTabStops(TraversalRequest request);
	}
	public interface IKeyboardInputSink {
		/// <summary>Registers the <see cref="T:System.Windows.Interop.IKeyboardInputSink" /> interface of a contained component. </summary>
		/// <param name="sink">The <see cref="T:System.Windows.Interop.IKeyboardInputSink" /> sink of the contained component.</param>
		/// <returns>The <see cref="T:System.Windows.Interop.IKeyboardInputSite" /> site of the contained component.</returns>
		// Token: 0x060010AC RID: 4268
		IKeyboardInputSite RegisterKeyboardInputSink(IKeyboardInputSink sink);

		/// <summary>Processes keyboard input at the keydown message level.</summary>
		/// <param name="msg">The message and associated data. Do not modify this structure. It is passed by reference for performance reasons only.</param>
		/// <param name="modifiers">Modifier keys.</param>
		/// <returns>
		///     <see langword="true" /> if the message was handled by the method implementation; otherwise, <see langword="false" />.</returns>
		// Token: 0x060010AD RID: 4269
		bool TranslateAccelerator(ref Message msg, VirtualKeyModifiers modifiers);

		/// <summary>Sets focus on either the first tab stop or the last tab stop of the sink. </summary>
		/// <param name="request">Specifies whether focus should be set to the first or the last tab stop.</param>
		/// <returns>
		///     <see langword="true" /> if the focus has been set as requested; <see langword="false" />, if there are no tab stops.</returns>
		// Token: 0x060010AE RID: 4270
		bool TabInto(TraversalRequest request);

		/// <summary>Gets or sets a reference to the component's container's <see cref="T:System.Windows.Interop.IKeyboardInputSite" /> interface. </summary>
		/// <returns>A reference to the container's <see cref="T:System.Windows.Interop.IKeyboardInputSite" /> interface.</returns>
		// Token: 0x170004DF RID: 1247
		// (get) Token: 0x060010AF RID: 4271
		// (set) Token: 0x060010B0 RID: 4272
		IKeyboardInputSite KeyboardInputSite { get; set; }

		/// <summary>Called when one of the mnemonics (access keys) for this sink is invoked. </summary>
		/// <param name="msg">The message for the mnemonic and associated data. Do not modify this message structure. It is passed by reference for performance reasons only.</param>
		/// <param name="modifiers">Modifier keys.</param>
		/// <returns>
		///     <see langword="true" /> if the message was handled; otherwise, <see langword="false" />.</returns>
		// Token: 0x060010B1 RID: 4273
		bool OnMnemonic(ref Message msg, VirtualKeyModifiers modifiers);

		/// <summary>Processes WM_CHAR, WM_SYSCHAR, WM_DEADCHAR, and WM_SYSDEADCHAR input messages before <see cref="M:System.Windows.Interop.IKeyboardInputSink.OnMnemonic(System.Windows.Interop.MSG@,System.Windows.Input.ModifierKeys)" /> is called. </summary>
		/// <param name="msg">The message and associated data. Do not modify this structure. It is passed by reference for performance reasons only.</param>
		/// <param name="modifiers">Modifier keys.</param>
		/// <returns>
		///     <see langword="true" /> if the message was processed and <see cref="M:System.Windows.Interop.IKeyboardInputSink.OnMnemonic(System.Windows.Interop.MSG@,System.Windows.Input.ModifierKeys)" /> should not be called; otherwise, <see langword="false" />.</returns>
		// Token: 0x060010B2 RID: 4274
		bool TranslateChar(ref Message msg, VirtualKeyModifiers modifiers);

		/// <summary>Gets a value that indicates whether the sink or one of its contained components has focus. </summary>
		/// <returns>
		///     <see langword="true" /> if the sink or one of its contained components has focus; otherwise, <see langword="false" />.</returns>
		// Token: 0x060010B3 RID: 4275
		bool HasFocusWithin();
	}
	public class TraversalRequest {
		/// <summary>Initializes a new instance of the <see cref="T:System.Windows.Input.TraversalRequest" /> class. </summary>
		/// <param name="focusNavigationDirection">The intended direction of the focus traversal, as a value of the enumeration.</param>
		// Token: 0x0600127B RID: 4731 RVA: 0x001253A8 File Offset: 0x001253A8
		public TraversalRequest(bool isBackward) {

			_focusNavigationDirection = isBackward;
		}

		/// <summary> Gets or sets a value that indicates whether focus traversal has reached the end of child elements that can have focus. </summary>
		/// <returns>
		///     <see langword="true" /> if this traversal has reached the end of child elements that can have focus; otherwise, <see langword="false" />. The default is <see langword="false" />.</returns>
		// Token: 0x1700052A RID: 1322
		// (get) Token: 0x0600127C RID: 4732 RVA: 0x001253F8 File Offset: 0x001253F8
		// (set) Token: 0x0600127D RID: 4733 RVA: 0x00125400 File Offset: 0x00125400
		public bool Wrapped {
			get {
				return _wrapped;
			}
			set {
				_wrapped = value;
			}
		}

		/// <summary>Gets the traversal direction. </summary>
		/// <returns>One of the traversal direction enumeration values.</returns>
		// Token: 0x1700052B RID: 1323
		// (get) Token: 0x0600127E RID: 4734 RVA: 0x0012540C File Offset: 0x0012540C
		public bool FocusNavigationDirection => _focusNavigationDirection;

		// Token: 0x0400116B RID: 4459
		private bool _wrapped;

		// Token: 0x0400116C RID: 4460
		private bool _focusNavigationDirection;
	}
	internal class HwndHostAutomationPeer : FrameworkElementAutomationPeer {
		// Token: 0x060049D3 RID: 18899 RVA: 0x008C5954 File Offset: 0x008C5954
		public HwndHostAutomationPeer(HwndHost owner)
			: base(owner) {
			//base.IsInteropPeer = true;
		}

		// Token: 0x060049D4 RID: 18900 RVA: 0x008C5964 File Offset: 0x008C5964
		protected override string GetClassNameCore() {
			return "HwndHost";
		}

		// Token: 0x060049D5 RID: 18901 RVA: 0x008C596C File Offset: 0x008C596C
		protected override AutomationControlType GetAutomationControlTypeCore() {
			return AutomationControlType.Pane;
		}

	}
}
