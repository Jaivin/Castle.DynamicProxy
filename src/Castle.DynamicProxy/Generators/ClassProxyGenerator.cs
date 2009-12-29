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

namespace Castle.DynamicProxy.Generators
{
	using System;
	using System.Collections.Generic;
	using System.Reflection;
#if !SILVERLIGHT
	using System.Xml.Serialization;
#endif
	using Castle.Core.Interceptor;
	using Castle.DynamicProxy.Generators.Emitters;
	using Castle.DynamicProxy.Generators.Emitters.SimpleAST;
	using Contributors;

	/// <summary>
	/// 
	/// </summary>
	public class ClassProxyGenerator : BaseProxyGenerator
	{
		public ClassProxyGenerator(ModuleScope scope, Type targetType) : base(scope, targetType)
		{
			CheckNotGenericTypeDefinition(targetType, "targetType");
			EnsureDoesNotImplementIProxyTargetAccessor(targetType, "targetType");
		}

		private void EnsureDoesNotImplementIProxyTargetAccessor(Type type, string name)
		{
			if (!typeof (IProxyTargetAccessor).IsAssignableFrom(type))
			{
				return;
			}
			var message = "Target type for the proxy implements IProxyTargetAccessor " +
			              "which is a DynamicProxy infrastructure interface and you should never implement it yourself. " +
			              "Are you trying to proxy an existing proxy?";
			throw new ArgumentException(message, name);
		}

		public Type GenerateCode(Type[] interfaces, ProxyGenerationOptions options)
		{
			// make sure ProxyGenerationOptions is initialized
			options.Initialize();

			CheckNotGenericTypeDefinitions(interfaces, "interfaces");
			Type proxyType;

			CacheKey cacheKey = new CacheKey(targetType, interfaces, options);

			using (var locker = Scope.Lock.ForReadingUpgradeable())
			{
				Type cacheType = GetFromCache(cacheKey);
				if (cacheType != null)
				{
					Logger.Debug("Found cached proxy type {0} for target type {1}.", cacheType.FullName, targetType.FullName);
					return cacheType;
				}

				// Upgrade the lock to a write lock, then read again. This is to avoid generating duplicate types
				// under heavy multithreaded load.
				locker.Upgrade();

				cacheType = GetFromCache(cacheKey);
				if (cacheType != null)
				{
					Logger.Debug("Found cached proxy type {0} for target type {1}.", cacheType.FullName, targetType.FullName);
					return cacheType;
				}

				// Log details about the cache miss
				Logger.Debug("No cached proxy type was found for target type {0}.", targetType.FullName);
				EnsureOptionsOverrideEqualsAndGetHashCode(options);

				ProxyGenerationOptions = options;

				var name = Scope.NamingScope.GetUniqueName("Castle.Proxies." + targetType.Name + "Proxy");
				proxyType = GenerateType(name, interfaces, Scope.NamingScope.SafeSubScope());

				AddToCache(cacheKey, proxyType);

			}

			return proxyType;
		}

		protected virtual Type GenerateType(string newName, Type[] interfaces, INamingScope namingScope)
		{
			IEnumerable<ITypeContributor> contributors;
			var implementedInterfaces = GetTypeImplementerMapping(interfaces, out contributors, namingScope);

			// Collect methods
			foreach (var contributor in contributors)
			{
				contributor.CollectElementsToProxy(ProxyGenerationOptions.Hook);
			}
			ProxyGenerationOptions.Hook.MethodsInspected();

			var emitter = BuildClassEmitter(newName, targetType, implementedInterfaces);
			CreateOptionsField(emitter);
			CreateSelectorField(emitter);
			emitter.AddCustomAttributes(ProxyGenerationOptions);

#if !SILVERLIGHT
			emitter.DefineCustomAttribute<XmlIncludeAttribute>(new object[] {targetType});
#endif
			// Fields generations
			FieldReference interceptorsField = CreateInterceptorsField(emitter);

			// Constructor
			var cctor = GenerateStaticConstructor(emitter);

			var constructorArguments = new List<FieldReference>();
			foreach (var contributor in contributors)
			{
				contributor.Generate(emitter, ProxyGenerationOptions);

				// TODO: redo it
				if (contributor is MixinContributor)
				{
					constructorArguments.AddRange((contributor as MixinContributor).Fields);
				}
			}

			// constructor arguments
			constructorArguments.Add(interceptorsField);
			var selector = emitter.GetField("__selector");
			if (selector != null)
			{
				constructorArguments.Add(selector);
			}

			GenerateConstructors(emitter, targetType, constructorArguments.ToArray());
			GenerateParameterlessConstructor(emitter, targetType, interceptorsField);

			// Complete type initializer code body
			CompleteInitCacheMethod(cctor.CodeBuilder);

			// Crosses fingers and build type

			Type proxyType = emitter.BuildType();
			InitializeStaticFields(proxyType);
			return proxyType;
		}

		protected virtual IEnumerable<Type> GetTypeImplementerMapping(Type[] interfaces, out IEnumerable<ITypeContributor> contributors, INamingScope namingScope)
		{
			var methodsToSkip = new List<MethodInfo>();
			var proxyInstance = new ClassProxyInstanceContributor(targetType, methodsToSkip, interfaces);
			// TODO: the trick with methodsToSkip is not very nice...
			var proxyTarget = new ClassProxyTargetContributor(targetType, methodsToSkip, namingScope) { Logger = Logger };
			IDictionary<Type, ITypeContributor> typeImplementerMapping = new Dictionary<Type, ITypeContributor>();

			// Order of interface precedence:
			// 1. first target
			// target is not an interface so we do nothing

			var targetInterfaces = TypeUtil.GetAllInterfaces(targetType);
			var additionalInterfaces = TypeUtil.GetAllInterfaces(interfaces);
			// 2. then mixins
			var mixins = new MixinContributor(namingScope, false);
			if (ProxyGenerationOptions.HasMixins)
			{
				foreach (var mixinInterface in ProxyGenerationOptions.MixinData.MixinInterfaces)
				{
					if (targetInterfaces.Contains(mixinInterface))
					{
						// OK, so the target implements this interface. We now do one of two things:
						if (additionalInterfaces.Contains(mixinInterface) && typeImplementerMapping.ContainsKey(mixinInterface) == false)
						{
							SafeAddMapping(mixinInterface, proxyTarget, typeImplementerMapping);
							proxyTarget.AddInterfaceToProxy(mixinInterface);
						}
						// we do not intercept the interface
						mixins.AddEmptyInterface(mixinInterface);
					}
					else
					{
						if (!typeImplementerMapping.ContainsKey(mixinInterface))
						{
							mixins.AddInterfaceToProxy(mixinInterface);
							SafeAddMapping(mixinInterface, mixins, typeImplementerMapping);
						}
					}
				}
			}
			var additionalInterfacesContributor = new InterfaceProxyWithoutTargetContributor(namingScope, (c, m) => NullExpression.Instance);
			// 3. then additional interfaces
			foreach (var @interface in additionalInterfaces)
			{
				if (targetInterfaces.Contains(@interface))
				{
					if (typeImplementerMapping.ContainsKey(@interface)) continue;

					// we intercept the interface, and forward calls to the target type
					SafeAddMapping(@interface, proxyTarget, typeImplementerMapping);
					proxyTarget.AddInterfaceToProxy(@interface);
				}
				else if (ProxyGenerationOptions.MixinData.ContainsMixin(@interface) == false)
				{
					additionalInterfacesContributor.AddInterfaceToProxy(@interface);
					AddMapping(@interface, additionalInterfacesContributor, typeImplementerMapping);
				}
			}
#if !SILVERLIGHT
			// 4. plus special interfaces
			if (targetType.IsSerializable)
			{
				AddMappingForISerializable(typeImplementerMapping, proxyInstance);
			}
#endif
			try
			{
				SafeAddMapping(typeof(IProxyTargetAccessor), proxyInstance, typeImplementerMapping);
			}
			catch (ArgumentException )
			{
				HandleExplicitlyPassedProxyTargetAccessor(targetInterfaces, additionalInterfaces);
			}

			contributors = new List<ITypeContributor>
			{
				proxyTarget,
				mixins,
				additionalInterfacesContributor,
				proxyInstance
			};
			return typeImplementerMapping.Keys;
		}
	}
}
