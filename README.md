## This is an add-in for [Fody](https://github.com/Fody/Fody/) 

![Icon](https://raw.github.com/Fody/MethodDecorator/master/Icons/package_icon.png)

Compile time decorator pattern via IL rewriting

[Introduction to Fody](http://github.com/Fody/Fody/wiki/SampleUsage)

## Nuget

Nuget package http://nuget.org/packages/MethodDecoratorExtension.Fody 

To Install from the Nuget Package Manager Console 
    
    PM> Install-Package MethodDecoratorExtension.Fody

### Base Decorator Code	
	
	[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor, AllowMultiple = true, Inherited = true)]
	public abstract class DecoratorAttribute : Attribute
	{
		public abstract void OnEntry(MethodBase method, object[] args);

		public abstract void OnExit(object returnValue, MethodBase method, object[] args);

		public abstract void OnException(Exception exception, MethodBase method, object[] args);
	}
	
### Your Code

Define your method decorators by deriving from ````DecoratorAttribute````:

	public class InterceptorAttribute : DecoratorAttribute
	{
	    public override void OnEntry(MethodBase method, object[] args)
	    {
	        TestMessages.Record(string.Format("OnEntry: {0}", method.DeclaringType.FullName + "." + method.Name));
	    }
	
	    public override void OnExit(object returnValue, MethodBase method, object[] args)
	    {
	        TestMessages.Record(string.Format("OnExit: {0}", method.DeclaringType.FullName + "." + method.Name));
	    }
	
	    public override void OnException(Exception exception, MethodBase method, object[] args)
	    {
	        TestMessages.Record(string.Format("OnException: {0} - {1}: {2}", method.DeclaringType.FullName + "." + method.Name, exception.GetType(), exception.Message));
	    }
	}
	
	public class Sample
	{
		[Interceptor]
		public void Method()
		{
		    Debug.WriteLine("Your Code");
		}
	}

### What gets compiled
	
	public class Sample
	{
		public object Method(object arg1, object arg2)
		{
		    MethodBase method = methodof(Sample.Method, Sample);
		    InterceptorAttribute attribute = (InterceptorAttribute) method.GetCustomAttributes(typeof(InterceptorAttribute), false)[0];
		    
			object[] args = new object[2] { (object) arg1, (object) arg2 };
			attribute.OnEntry(method, args);
		    try
		    {
		        Debug.WriteLine("Your Code");
				object returnValue = ...; // null if void method
		        attribute.OnExit(returnValue, method, args);
				return returnValue;
		    }
		    catch (Exception exception)
		    {
		        attribute.OnException(exception, method, args);
		        throw;
		    }
		}
	}

## Known limitations

- Does not support method with multiple return instructions
- No log when compiling

## Licence

The MIT License

Copyright (c) Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.

