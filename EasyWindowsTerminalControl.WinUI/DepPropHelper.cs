using System;
using System.Linq.Expressions;
using System.Reflection;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;


namespace EasyWindowsTerminalControl.Internals {

	internal class DepPropHelper<CONTROL_TYPE> where CONTROL_TYPE : UserControl {
		protected DepPropHelper() => throw new Exception("Should not be instanced");
		public static DependencyProperty GenerateWriteOnlyProperty<PROP_TYPE>(Expression<Func<CONTROL_TYPE, PROP_TYPE>> PropToSet) {

			var me = PropToSet.Body as MemberExpression;
			if (me == null)
				throw new ArgumentException(nameof(PropToSet));
			var propName = me.Member.Name;
			var prop = typeof(CONTROL_TYPE).GetProperty(me.Member.Name, BindingFlags.Instance | BindingFlags.Public);

			if (prop == null)
				throw new ArgumentException(nameof(PropToSet));

			return DependencyProperty.Register(propName, typeof(PROP_TYPE), typeof(CONTROL_TYPE), new PropertyMetadata(null, (target, value) => CoerceReadOnlyHandle(prop.SetMethod, target, value)));
		}
		private static object CoerceReadOnlyHandle(MethodInfo SetMethod, DependencyObject target, object value) {
			SetMethod.Invoke(target, new object[] { value });
			return null;
		}
	}

}
