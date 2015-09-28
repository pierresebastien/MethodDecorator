using System;
using System.Reflection;

namespace MethodDecorator.Attributes
{
	[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor, AllowMultiple = true, Inherited = true)]
	public abstract class DecoratorAttribute : Attribute
	{
		public abstract void OnEntry(MethodBase method, object[] args);

		public abstract void OnExit(object returnValue, MethodBase method, object[] args);

		public abstract void OnException(Exception exception, MethodBase method, object[] args);
	}
}
