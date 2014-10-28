using System;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MethodDecorator.Fody.Helpers
{
	public static class CecilHelper
	{
		public static VariableDefinition AddVariable(this MethodDefinition method, TypeReference typeReference, string variableName = null)
		{
			VariableDefinition variableDefinition = method.Module.ImportVariable(typeReference, variableName);
			method.Body.Variables.Add(variableDefinition);
			return variableDefinition;
		}

		public static TypeReference ImportType(this ModuleDefinition module, Type type)
		{
			return module.Import(type);
		}

		public static VariableDefinition ImportVariable(this ModuleDefinition module, TypeReference typeReference,
		                                                string variableName = null)
		{
			return string.IsNullOrWhiteSpace(variableName)
				       ? new VariableDefinition(typeReference)
				       : new VariableDefinition(variableName, typeReference);
		}
	}
}
