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
	using Castle.DynamicProxy.Generators;
	using Generators.Emitters;
	using Generators.Emitters.SimpleAST;

	public abstract class MixinContributorBase : ITypeContributor
	{
		protected FieldReference field;

		protected Type mixinInterface;



		public FieldReference BackingField
		{
			get
			{
				return field;
			}
		}

		protected FieldReference BuildTargetField(ClassEmitter emitter, Type type)
		{
			return emitter.CreateField("__mixin_" + type.FullName.Replace(".", "_"), type);
		}

		public abstract void CollectElementsToProxy(IProxyGenerationHook hook);

		public abstract void Generate(ClassEmitter @class, ProxyGenerationOptions options);

		// TODO: this should be moved to some utility class or common base...
		protected void ReplicateNonInheritableAttributes(MethodInfo method, MethodEmitter emitter, ProxyGenerationOptions options)
		{
			object[] attrs = method.GetCustomAttributes(false);

			foreach (Attribute attribute in attrs)
			{
				if (ShouldSkipAttributeReplication(attribute)) continue;

				emitter.DefineCustomAttribute(attribute, options.AttributeDisassembler);
			}
		}

		private static bool SpecialCaseAttributThatShouldNotBeReplicated(Attribute attribute)
		{
			return AttributesToAvoidReplicating.Contains(attribute.GetType());
		}

		/// <summary>
		/// Attributes should be replicated if they are non-inheritable,
		/// but there are some special cases where the attributes means
		/// something to the CLR, where they should be skipped.
		/// </summary>
		private bool ShouldSkipAttributeReplication(Attribute attribute)
		{
			if (SpecialCaseAttributThatShouldNotBeReplicated(attribute))
				return true;

			object[] attrs = attribute.GetType()
				.GetCustomAttributes(typeof(AttributeUsageAttribute), true);

			if (attrs.Length != 0)
			{
				var usage = (AttributeUsageAttribute)attrs[0];

				return usage.Inherited;
			}

			return true;
		}
	}
}