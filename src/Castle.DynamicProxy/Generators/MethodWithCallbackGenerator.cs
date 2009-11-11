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
using System;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml.Serialization;

using Castle.Core.Interceptor;
using Castle.DynamicProxy.Generators.Emitters;
using Castle.DynamicProxy.Generators.Emitters.SimpleAST;
using Castle.DynamicProxy.Tokens;

namespace Castle.DynamicProxy.Generators
{
	using Castle.DynamicProxy.Contributors;

	public class MethodWithCallbackGenerator:MethodGenerator
	{
		private readonly MethodToGenerate method;
		private readonly NestedClassEmitter invocation;
		private readonly Reference interceptors;
		private readonly CreateMethodDelegate createMethod;

		public MethodWithCallbackGenerator(MethodToGenerate method, NestedClassEmitter invocation, Reference interceptors, CreateMethodDelegate createMethod)
		{
			this.method = method;
			this.invocation = invocation;
			this.interceptors = interceptors;
			this.createMethod = createMethod;
		}

		public override MethodEmitter Generate(ClassEmitter @class, ProxyGenerationOptions options, INamingScope namingScope)
		{
			string name;
			var atts = GeneratorUtil.ObtainClassMethodAttributes(out name, method);
			var methodEmitter = createMethod(name, atts);
			var proxiedMethod = ImplementProxiedMethod(methodEmitter,
			                                           @class,
			                                           options,
			                                           namingScope);

			if (method.Method.DeclaringType.IsInterface)
			{
				@class.TypeBuilder.DefineMethodOverride(methodEmitter.MethodBuilder, method.Method);

			}
			return proxiedMethod;
		}

		private MethodEmitter ImplementProxiedMethod(MethodEmitter emitter, ClassEmitter @class, ProxyGenerationOptions options,INamingScope namingScope)
		{
			var methodInfo = method.Method;
			emitter.CopyParametersAndReturnTypeFrom(methodInfo, @class);

			TypeReference[] dereferencedArguments = IndirectReference.WrapIfByRef(emitter.Arguments);

			Type iinvocation = invocation.TypeBuilder;

			Trace.Assert(methodInfo.IsGenericMethod == iinvocation.IsGenericTypeDefinition);
			bool isGenericInvocationClass = false;
			Type[] genericMethodArgs = Type.EmptyTypes;
			if (methodInfo.IsGenericMethod)
			{
				// bind generic method arguments to invocation's type arguments
				genericMethodArgs = emitter.MethodBuilder.GetGenericArguments();
				iinvocation = iinvocation.MakeGenericType(genericMethodArgs);
				isGenericInvocationClass = true;
			}

			Expression methodInfoTokenExp;

			string tokenFieldName;
			if (methodInfo.IsGenericMethod)
			{
				// Not in the cache: generic method
				MethodInfo genericMethod = methodInfo.MakeGenericMethod(genericMethodArgs);

				tokenFieldName = namingScope.GetUniqueName("token_" + methodInfo.Name);
				methodInfoTokenExp = new MethodTokenExpression(genericMethod);
			}
			else
			{

				var methodTokenField = @class.CreateStaticField(namingScope.GetUniqueName("token_" + methodInfo.Name),
				                                                typeof (MethodInfo));
				@class.ClassConstructor.CodeBuilder.AddStatement(new AssignStatement(methodTokenField, new MethodTokenExpression(methodInfo)));
				tokenFieldName = methodTokenField.Reference.Name;
				methodInfoTokenExp = methodTokenField.ToExpression();
			}

			LocalReference invocationImplLocal = emitter.CodeBuilder.DeclareLocal(iinvocation);

			// TODO: Initialize iinvocation instance with ordinary arguments and in and out arguments

			ConstructorInfo constructor = invocation.Constructors[0].ConstructorBuilder;
			if (isGenericInvocationClass)
			{
				constructor = TypeBuilder.GetConstructor(iinvocation, constructor);
			}

			Expression targetMethod = methodInfoTokenExp;
			Expression interfaceMethod = NullExpression.Instance;


			NewInstanceExpression newInvocImpl;

			// TODO: this is not always true. Should be passed explicitly as ctor parameter
			Type targetType = methodInfo.DeclaringType;
			Debug.Assert(targetType != null, "targetType != null");
			if (options.Selector == null)
			{
				newInvocImpl = //actual contructor call
					new NewInstanceExpression(constructor,
					                          SelfReference.Self.ToExpression(),
					                          SelfReference.Self.ToExpression(),
					                          interceptors.ToExpression(),
					                          // NOTE: there's no need to cache type token
					                          // profiling showed that it gives no real performance gain
					                          new TypeTokenExpression(targetType),
					                          targetMethod,
					                          interfaceMethod,
					                          new ReferencesToObjectArrayExpression(dereferencedArguments));
			}
			else
			{
				// Create the field to store the selected interceptors for this method if an InterceptorSelector is specified
				// NOTE: If no interceptors are returned, should we invoke the base.Method directly? Looks like we should not.
				var methodInterceptors = @class.CreateField(string.Format("{0}_interceptors", tokenFieldName),
				                                            typeof (IInterceptor[]), false);

#if !SILVERLIGHT
				@class.DefineCustomAttributeFor<XmlIgnoreAttribute>(methodInterceptors);
#endif

				MethodInvocationExpression selector =
					new MethodInvocationExpression(@class.GetField("proxyGenerationOptions"),
					                               ProxyGenerationOptionsMethods.GetSelector);
				selector.VirtualCall = true;

				newInvocImpl = //actual contructor call
					new NewInstanceExpression(constructor,
					                          SelfReference.Self.ToExpression(),
					                          SelfReference.Self.ToExpression(),
					                          interceptors.ToExpression(),
					                          new TypeTokenExpression(targetType),
					                          targetMethod,
					                          interfaceMethod,
					                          new ReferencesToObjectArrayExpression(dereferencedArguments),
					                          selector,
					                          new AddressOfReferenceExpression(methodInterceptors));
			}

			emitter.CodeBuilder.AddStatement(new AssignStatement(invocationImplLocal, newInvocImpl));

			if (methodInfo.ContainsGenericParameters)
			{
				EmitLoadGenricMethodArguments(emitter, methodInfo.MakeGenericMethod(genericMethodArgs), invocationImplLocal);
			}

			emitter.CodeBuilder.AddStatement(
				new ExpressionStatement(new MethodInvocationExpression(invocationImplLocal, InvocationMethods.Proceed)));

			GeneratorUtil.CopyOutAndRefParameters(dereferencedArguments, invocationImplLocal, methodInfo, emitter);

			if (methodInfo.ReturnType != typeof(void))
			{
				// Emit code to return with cast from ReturnValue
				MethodInvocationExpression getRetVal =
					new MethodInvocationExpression(invocationImplLocal, InvocationMethods.GetReturnValue);

				emitter.CodeBuilder.AddStatement(
					new ReturnStatement(new ConvertExpression(emitter.ReturnType, getRetVal)));
			}
			else
			{
				emitter.CodeBuilder.AddStatement(new ReturnStatement());
			}

			return emitter;
		}

		private void EmitLoadGenricMethodArguments(MethodEmitter methodEmitter, MethodInfo method, Reference invocationImplLocal)
		{
#if SILVERLIGHT
			Type[] genericParameters =
				Castle.Core.Extensions.SilverlightExtensions.FindAll(method.GetGenericArguments(), delegate(Type t) { return t.IsGenericParameter; });
#else
			Type[] genericParameters = Array.FindAll(method.GetGenericArguments(), t => t.IsGenericParameter);
#endif
			LocalReference genericParamsArrayLocal = methodEmitter.CodeBuilder.DeclareLocal(typeof(Type[]));
			methodEmitter.CodeBuilder.AddStatement(
				new AssignStatement(genericParamsArrayLocal, new NewArrayExpression(genericParameters.Length, typeof(Type))));

			for (int i = 0; i < genericParameters.Length; ++i)
			{
				methodEmitter.CodeBuilder.AddStatement(
					new AssignArrayStatement(genericParamsArrayLocal, i, new TypeTokenExpression(genericParameters[i])));
			}
			methodEmitter.CodeBuilder.AddStatement(new ExpressionStatement(
			                                       	new MethodInvocationExpression(invocationImplLocal, InvocationMethods.SetGenericMethodArguments,
			                                       	                               new ReferenceExpression(
			                                       	                               	genericParamsArrayLocal))));
		}


	}
}