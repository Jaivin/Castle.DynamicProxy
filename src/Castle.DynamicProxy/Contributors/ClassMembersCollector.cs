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

namespace Castle.DynamicProxy.Contributors
{
	using System;
	using System.Reflection;
	using Generators;

	public class ClassMembersCollector : MembersCollector
	{
		public ClassMembersCollector(Type targetType, ITypeContributor targetContributor)
			: base(targetType, targetContributor, true, new InterfaceMapping())
		{

		}

		protected override MethodToGenerate GetMethodToGenerate(MethodInfo method, IProxyGenerationHook hook, bool isStandalone)
		{
			var accepted = AcceptMethod(method, onlyProxyVirtual, hook);
			if (!accepted && !method.IsAbstract)
			{
				//we don't need to do anything...
				return null;
			}

			ITypeContributor target = null;
			if(!method.IsAbstract)
			{
				target = contributor;
			}

			return new MethodToGenerate(method, isStandalone, target, method, accepted);
		}

	}
}