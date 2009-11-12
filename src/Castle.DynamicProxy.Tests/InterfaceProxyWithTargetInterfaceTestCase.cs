// Copyright 2004-2009 Castle Project - http://www.castleproject.org/
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace Castle.DynamicProxy.Tests
{
	using System;

	using Castle.DynamicProxy.Tests.Interfaces;

	using Core.Interceptor;
	using NUnit.Framework;

	[TestFixture]
	public class InterfaceProxyWithTargetInterfaceTestCase : BasePEVerifyTestCase
	{
		[Test]
		public void When_target_does_not_implement_additional_interface_method_should_throw()
		{
			var proxy = generator.CreateInterfaceProxyWithTargetInterface(typeof (IOne),
																		  new[] {typeof (ITwo)},
																		  new One(),
																		  ProxyGenerationOptions.Default,new StandardInterceptor());
			Assert.Throws(typeof(NotImplementedException),() => (proxy as ITwo).TwoMethod());
		}

		[Test]
		public void When_target_does_implement_additional_interface_method_should_forward()
		{
			var proxy = generator.CreateInterfaceProxyWithTargetInterface(typeof(IOne),
																		  new[] { typeof(ITwo) },
																		  new OneTwo(),
																		  ProxyGenerationOptions.Default);
			int result = (proxy as ITwo).TwoMethod();
			Assert.AreEqual(2, result);
		}

		[Test]
		public void TargetInterface_methods_should_be_forwarded_to_target()
		{
			var proxy = generator.CreateInterfaceProxyWithTargetInterface(typeof(IOne),
																		  new[] { typeof(ITwo) },
																		  new OneTwo(),
																		  ProxyGenerationOptions.Default);
			int result = (proxy as IOne).OneMethod();
			Assert.AreEqual(3, result);
		}

		[Test]
		[Ignore("DYNPROXY-ISSUE-121")]
		public void Mixin_methods_should_be_forwarded_to_target_if_implements_mixin_interface()
		{
			var options = new ProxyGenerationOptions();
			options.AddMixinInstance(new Two());
			var proxy = generator.CreateInterfaceProxyWithTargetInterface(typeof(IOne),
			                                                              new OneTwo(),
			                                                              options);
			int result = (proxy as ITwo).TwoMethod();
			Assert.AreEqual(2, result);
		}

	}
}
