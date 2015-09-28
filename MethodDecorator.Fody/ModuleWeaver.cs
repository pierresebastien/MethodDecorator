﻿using System;
using System.Collections.Generic;
using System.Linq;
using MethodDecorator.Fody.Extensions;
using Mono.Cecil;

namespace MethodDecorator.Fody
{
	// TODO: Add log
	public class ModuleWeaver
	{
		private class AttributeMethodInfo
		{
			public TypeDefinition TypeDefinition { get; set; }
			public MethodDefinition MethodDefinition { get; set; }
			public CustomAttribute CustomAttribute { get; set; }
		}

		public ModuleDefinition ModuleDefinition { get; set; }
		public IAssemblyResolver AssemblyResolver { get; set; }

		public void Execute()
		{
			var methodDecoratorAttribute = Type.GetType("MethodDecorator.Attributes.DecoratorAttribute, MethodDecorator.Attributes");
			if (methodDecoratorAttribute != null)
			{
				var decoratorTypeDefinitions = FindAttributeTypes(methodDecoratorAttribute);
				if (decoratorTypeDefinitions.Any())
				{
					var decorator = new MethodDecorator(ModuleDefinition);
					var methods = FindAttributedMethods(decoratorTypeDefinitions);
					foreach (var method in methods)
					{
						decorator.Decorate(method.MethodDefinition, method.CustomAttribute, method.TypeDefinition);
					}
				}
			}
		}

		private IList<TypeDefinition> FindAttributeTypes(Type type)
		{
			var allAttributes = ModuleDefinition.Types.Where(c => c.DerivesFrom(type));
			return (from t in allAttributes where !t.IsAbstract select t).ToList();
		}

		private IEnumerable<AttributeMethodInfo> FindAttributedMethods(IEnumerable<TypeDefinition> markerTypeDefintions)
		{
			return from topLevelType in ModuleDefinition.Types
			       from type in GetAllTypes(topLevelType)
			       from method in type.Methods
			       where method.HasBody
			       from attribute in method.CustomAttributes
			       let attributeTypeDef = attribute.AttributeType.Resolve()
			       from markerTypeDefinition in markerTypeDefintions
			       where attributeTypeDef.DerivesFrom(markerTypeDefinition) ||
			             attributeTypeDef.FullName == markerTypeDefinition.FullName
			       select new AttributeMethodInfo
				              {
					              CustomAttribute = attribute,
					              TypeDefinition = type,
					              MethodDefinition = method
				              };
		}

		private static IEnumerable<TypeDefinition> GetAllTypes(TypeDefinition type)
		{
			yield return type;

			IEnumerable<TypeDefinition> allNestedTypes = from t in type.NestedTypes
			                                             from t2 in GetAllTypes(t)
			                                             select t2;

			foreach (TypeDefinition t in allNestedTypes)
				yield return t;
		}
	}
}