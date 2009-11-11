﻿// Copyright 2004-2009 Castle Project - http://www.castleproject.org/
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
	using System.Reflection;

	using Castle.DynamicProxy.Generators;
	using Castle.DynamicProxy.Generators.Emitters;
	using Castle.DynamicProxy.Generators.Emitters.SimpleAST;

	public class MinimialisticMethodGenerator:MethodGenerator
	{
		private readonly MethodToGenerate method;
		private readonly CreateMethodDelegate createMethod;
		private readonly GetMethodAttributesDelegate getAttributes;

		public MinimialisticMethodGenerator(MethodToGenerate method, CreateMethodDelegate createMethod, GetMethodAttributesDelegate getAttributes)
		{
			this.method = method;
			this.createMethod = createMethod;
			this.getAttributes = getAttributes;
		}


		public override MethodEmitter Generate(ClassEmitter @class, ProxyGenerationOptions options, INamingScope namingScope)
		{

			string name;
			MethodAttributes atts = getAttributes(out name, method);
			MethodEmitter methodEmitter = createMethod(name, atts);
			MethodEmitter proxiedMethod = ImplementProxiedMethod(methodEmitter, @class, options, namingScope);

			@class.TypeBuilder.DefineMethodOverride(methodEmitter.MethodBuilder, method.Method);
			return proxiedMethod;
		}

		protected MethodEmitter ImplementProxiedMethod(MethodEmitter emitter, ClassEmitter @class, ProxyGenerationOptions options, INamingScope namingScope)
		{
			emitter.CopyParametersAndReturnTypeFrom(method.Method, @class);
			var parameters = method.Method.GetParameters();
			for (int index = 0; index < parameters.Length; index++)
			{
				var parameter = parameters[index];
				if (parameter.IsOut)
				{
					emitter.CodeBuilder.AddStatement(
						new AssignArgumentStatement(new ArgumentReference(parameter.ParameterType, index + 1),
						                    new DefaultValueExpression(parameter.ParameterType)));
				}
			}
			if(emitter.ReturnType==typeof(void))
			{
				emitter.CodeBuilder.AddStatement(new ReturnStatement());
			}
			else
			{
				emitter.CodeBuilder.AddStatement(new ReturnStatement(new DefaultValueExpression(emitter.ReturnType)));
			}

			return emitter;
		}
	}
}