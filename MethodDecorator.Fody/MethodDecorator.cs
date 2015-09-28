using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MethodDecorator.Fody.Extensions;
using MethodDecorator.Fody.Helpers;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MethodDecorator.Fody
{
	// TODO: add support for multiple return instructions
	public class MethodDecorator
	{
		private readonly ReferenceFinder _referenceFinder;

		public MethodDecorator(ModuleDefinition moduleDefinition)
		{
			_referenceFinder = new ReferenceFinder(moduleDefinition);
		}

		public void Decorate(MethodDefinition method, CustomAttribute attribute, TypeDefinition typeDefinition)
		{
			method.Body.InitLocals = true;

			// ref to retrieve method and custom attribute
			var getMethodFromHandleRef =
				_referenceFinder.GetMethodReference(typeof (MethodBase),
				                                    md => md.Name == "GetMethodFromHandle" && md.Parameters.Count == 2);
			var getCustomAttributesRef =
				_referenceFinder.GetMethodReference(typeof (MemberInfo),
				                                    md => md.Name == "GetCustomAttributes" && md.Parameters.Count == 2);
			var getTypeFromHandleRef = _referenceFinder.GetMethodReference(typeof (Type), md => md.Name == "GetTypeFromHandle");

			// types ref
			var methodBaseTypeRef = _referenceFinder.GetTypeReference(typeof (MethodBase));
			var exceptionTypeRef = _referenceFinder.GetTypeReference(typeof (Exception));
			var parameterTypeRef = _referenceFinder.GetTypeReference(typeof (object));
			var parametersArrayTypeRef = _referenceFinder.GetTypeReference(typeof (object[]));

			// variable definitions
			var methodVariableDefinition = method.AddVariable(methodBaseTypeRef, "__fody$method");
			var attributeVariableDefinition = method.AddVariable(attribute.AttributeType, "__fody$attribute");
			var exceptionVariableDefinition = method.AddVariable(exceptionTypeRef, "__fody$exception");
			var parametersVariableDefinition = method.AddVariable(parametersArrayTypeRef, "__fody$parameters");

			// return variable
			VariableDefinition retvalVariableDefinition = null;
			if (method.ReturnType.FullName != method.Module.ImportType(typeof (void)).FullName)
			{
				retvalVariableDefinition = method.AddVariable(method.ReturnType, "__fody$retval");
			}

			// method decorator method refs
			var onEntryMethodRef = _referenceFinder.GetMethodReference(attribute.AttributeType, md => md.Name == "OnEntry");
			var onExitMethodRef = _referenceFinder.GetMethodReference(attribute.AttributeType, md => md.Name == "OnExit");
			var onExceptionMethodRef = _referenceFinder.GetMethodReference(attribute.AttributeType,
			                                                               md => md.Name == "OnException");

			// get processor and first instruction
			var processor = method.Body.GetILProcessor();
			var methodBodyFirstInstruction = method.Body.Instructions.First();
			if (method.IsConstructor)
			{
				methodBodyFirstInstruction = method.Body.Instructions.First(i => i.OpCode == OpCodes.Call).Next;
			}

			// create instructions
			var getAttributeInstanceInstructions =
				GetAttributeInstanceInstructions(processor, method, attribute, attributeVariableDefinition, methodVariableDefinition,
				                                 getCustomAttributesRef, getTypeFromHandleRef, getMethodFromHandleRef);
			var createParametersArrayInstructions =
				CreateParametersArrayInstructions(processor, method, parameterTypeRef, parametersVariableDefinition);
			var callOnEntryInstructions =
				GetCallOnEntryInstructions(processor, attributeVariableDefinition, methodVariableDefinition,
				                           parametersVariableDefinition, onEntryMethodRef);
			var saveRetvalInstructions = GetSaveRetvalInstructions(processor, retvalVariableDefinition);
			var callOnExitInstructions =
				GetCallOnExitInstructions(processor, attributeVariableDefinition, methodVariableDefinition,
										  parametersVariableDefinition, retvalVariableDefinition ?? method.AddVariable(parameterTypeRef), onExitMethodRef);
			var methodBodyReturnInstructions = GetMethodBodyReturnInstructions(processor, retvalVariableDefinition);
			var methodBodyReturnInstruction = methodBodyReturnInstructions.First();
			var tryCatchLeaveInstructions = GetTryCatchLeaveInstructions(processor, methodBodyReturnInstruction);
			var catchHandlerInstructions =
				GetCatchHandlerInstructions(processor, attributeVariableDefinition, exceptionVariableDefinition,
				                            methodVariableDefinition, parametersVariableDefinition, onExceptionMethodRef);

			ReplaceRetInstructions(processor, saveRetvalInstructions.Concat(callOnExitInstructions).First());

			// insert instructions
			processor.InsertBefore(methodBodyFirstInstruction, getAttributeInstanceInstructions);
			processor.InsertBefore(methodBodyFirstInstruction, createParametersArrayInstructions);
			processor.InsertBefore(methodBodyFirstInstruction, callOnEntryInstructions);

			// TODO: ICollection<Instruction> returnInstructions =
			// method.Body.Instructions.ToList().Where(x => x.OpCode == OpCodes.Ret).ToList();

			processor.InsertAfter(method.Body.Instructions.Last(), methodBodyReturnInstructions);
			processor.InsertBefore(methodBodyReturnInstruction, saveRetvalInstructions);
			processor.InsertBefore(methodBodyReturnInstruction, callOnExitInstructions);
			processor.InsertBefore(methodBodyReturnInstruction, tryCatchLeaveInstructions);
			processor.InsertBefore(methodBodyReturnInstruction, catchHandlerInstructions);
			method.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
				                                  {
					                                  CatchType = exceptionTypeRef,
					                                  TryStart = methodBodyFirstInstruction,
					                                  TryEnd = tryCatchLeaveInstructions.Last().Next,
					                                  HandlerStart = catchHandlerInstructions.First(),
					                                  HandlerEnd = catchHandlerInstructions.Last().Next
				                                  });
		}

		private static IEnumerable<Instruction> GetAttributeInstanceInstructions(ILProcessor processor,
		                                                                         MethodReference method,
		                                                                         ICustomAttribute attribute,
		                                                                         VariableDefinition
			                                                                         attributeVariableDefinition,
		                                                                         VariableDefinition methodVariableDefinition,
		                                                                         MethodReference getCustomAttributesRef,
		                                                                         MethodReference getTypeFromHandleRef,
		                                                                         MethodReference getMethodFromHandleRef)
		{
			// Get the attribute instance (this gets a new instance for each invocation.
			// Might be better to create a static class that keeps a track of a single
			// instance per method and we just refer to that)
			return new List<Instruction>
				       {
					       processor.Create(OpCodes.Ldtoken, method),
					       processor.Create(OpCodes.Ldtoken, method.DeclaringType),
					       processor.Create(OpCodes.Call, getMethodFromHandleRef),
					       // Push method onto the stack, GetMethodFromHandle, result on stack
					       processor.Create(OpCodes.Stloc_S, methodVariableDefinition), // Store method in __fody$method
					       processor.Create(OpCodes.Ldloc_S, methodVariableDefinition),
					       processor.Create(OpCodes.Ldtoken, attribute.AttributeType),
					       processor.Create(OpCodes.Call, getTypeFromHandleRef),
					       // Push method + attribute onto the stack, GetTypeFromHandle, result on stack
					       processor.Create(OpCodes.Ldc_I4_0),
					       processor.Create(OpCodes.Callvirt, getCustomAttributesRef),
					       // Push false onto the stack (result still on stack), GetCustomAttributes
					       processor.Create(OpCodes.Ldc_I4_0),
					       processor.Create(OpCodes.Ldelem_Ref), // Get 0th index from result
					       processor.Create(OpCodes.Castclass, attribute.AttributeType),
					       processor.Create(OpCodes.Stloc_S, attributeVariableDefinition) // Cast to attribute stor in __fody$attribute
				       };
		}

		private static IEnumerable<Instruction> CreateParametersArrayInstructions(ILProcessor processor,
		                                                                          MethodDefinition method,
		                                                                          TypeReference objectTypeReference,
		                                                                          VariableDefinition arrayVariable
			/*parameters*/)
		{
			var createArray = new List<Instruction>
				                  {
					                  processor.Create(OpCodes.Ldc_I4, method.Parameters.Count), //method.Parameters.Count
					                  processor.Create(OpCodes.Newarr, objectTypeReference), // new object[method.Parameters.Count]
					                  processor.Create(OpCodes.Stloc, arrayVariable)
					                  // var objArray = new object[method.Parameters.Count]
				                  };

			foreach (var p in method.Parameters)
			{
				createArray.AddRange(ILHelper.ProcessParam(p, arrayVariable));
			}

			return createArray;
		}

		private static IEnumerable<Instruction> GetCallOnEntryInstructions(ILProcessor processor,
		                                                                   VariableDefinition attributeVariableDefinition,
		                                                                   VariableDefinition methodVariableDefinition,
		                                                                   VariableDefinition parametersVariableDefinition,
		                                                                   MethodReference onEntryMethodRef)
		{
			// Call __fody$attribute.OnEntry("{methodName}")
			return new List<Instruction>
				       {
					       processor.Create(OpCodes.Ldloc_S, attributeVariableDefinition),
					       processor.Create(OpCodes.Ldloc_S, methodVariableDefinition),
					       processor.Create(OpCodes.Ldloc_S, parametersVariableDefinition),
					       processor.Create(OpCodes.Callvirt, onEntryMethodRef)
				       };
		}

		// TODO: save null in retvaldefinition
		private static IList<Instruction> GetSaveRetvalInstructions(ILProcessor processor,
		                                                            VariableDefinition retvalVariableDefinition)
		{
			return retvalVariableDefinition == null || processor.Body.Instructions.All(i => i.OpCode != OpCodes.Ret)
				       ? new Instruction[0]
				       : new[] {processor.Create(OpCodes.Stloc_S, retvalVariableDefinition)};
		}

		private static IList<Instruction> GetMethodBodyReturnInstructions(ILProcessor processor,
		                                                                  VariableDefinition retvalVariableDefinition)
		{
			var instructions = new List<Instruction>();
			if (retvalVariableDefinition != null)
			{
				instructions.Add(processor.Create(OpCodes.Ldloc_S, retvalVariableDefinition));
			}
			instructions.Add(processor.Create(OpCodes.Ret));
			return instructions;
		}

		private static IList<Instruction> GetCallOnExitInstructions(ILProcessor processor,
		                                                            VariableDefinition attributeVariableDefinition,
		                                                            VariableDefinition methodVariableDefinition,
		                                                            VariableDefinition parametersVariableDefinition,
		                                                            VariableDefinition retvalVariableDefinition,
		                                                            MethodReference onExitMethodRef)
		{
			// Call __fody$attribute.OnExit("{methodName}")
			return new List<Instruction>
				       {
					       processor.Create(OpCodes.Ldloc_S, attributeVariableDefinition),
					       processor.Create(OpCodes.Ldloc_S, retvalVariableDefinition),
					       processor.Create(OpCodes.Ldloc_S, methodVariableDefinition),
					       processor.Create(OpCodes.Ldloc_S, parametersVariableDefinition),
					       processor.Create(OpCodes.Callvirt, onExitMethodRef)
				       };
		}

		private static void ReplaceRetInstructions(ILProcessor processor, Instruction methodEpilogueFirstInstruction)
		{
			// We cannot call ret inside a try/catch block. Replace all ret instructions with
			// an unconditional branch to the start of the OnExit epilogue
			var retInstructions = (from i in processor.Body.Instructions
			                       where i.OpCode == OpCodes.Ret
			                       select i).ToList();

			foreach (var instruction in retInstructions)
			{
				instruction.OpCode = OpCodes.Br_S;
				instruction.Operand = methodEpilogueFirstInstruction;
			}
		}

		private static IList<Instruction> GetTryCatchLeaveInstructions(ILProcessor processor,
		                                                               Instruction methodBodyReturnInstruction)
		{
			return new[] {processor.Create(OpCodes.Leave_S, methodBodyReturnInstruction)};
		}

		private static List<Instruction> GetCatchHandlerInstructions(ILProcessor processor,
		                                                             VariableDefinition attributeVariableDefinition,
		                                                             VariableDefinition exceptionVariableDefinition,
		                                                             VariableDefinition methodVariableDefinition,
		                                                             VariableDefinition parametersVariableDefinition,
		                                                             MethodReference onExceptionMethodRef)
		{
			// Store the exception in __fody$exception
			// Call __fody$attribute.OnExcetion("{methodName}", __fody$exception)
			// rethrow
			return new List<Instruction>
				       {
					       processor.Create(OpCodes.Stloc_S, exceptionVariableDefinition),
					       processor.Create(OpCodes.Ldloc_S, attributeVariableDefinition),
					       processor.Create(OpCodes.Ldloc_S, exceptionVariableDefinition),
					       processor.Create(OpCodes.Ldloc_S, methodVariableDefinition),
					       processor.Create(OpCodes.Ldloc_S, parametersVariableDefinition),
					       processor.Create(OpCodes.Callvirt, onExceptionMethodRef),
					       processor.Create(OpCodes.Rethrow)
				       };
		}
	}
}