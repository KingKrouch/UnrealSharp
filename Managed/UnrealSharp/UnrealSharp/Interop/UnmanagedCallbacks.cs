﻿using System.Reflection;
using System.Runtime.InteropServices;
using UnrealSharp.Attributes;
using UnrealSharp.Logging;

namespace UnrealSharp.Interop;

public static class UnmanagedCallbacks
{
    [UnmanagedCallersOnly]
    internal static IntPtr CreateNewManagedObject(IntPtr nativeObject, IntPtr typeHandle)
    {
        try
        {
            if (nativeObject == IntPtr.Zero)
            {
                throw new ArgumentNullException(nameof(nativeObject));
            }
            
            RuntimeTypeHandle handle = RuntimeTypeHandle.FromIntPtr(typeHandle);

            if (handle == null)
            {
                throw new ArgumentNullException(nameof(nativeObject));
            }

            Type? typeToCreate = Type.GetTypeFromHandle(handle);

            if (typeToCreate == null)
            {
                throw new ArgumentNullException(nameof(nativeObject));
            }

            return UnrealSharpObject.Create(typeToCreate, nativeObject);
        }
        catch (Exception ex)
        {
            LogUnrealSharp.Log($"Failed to create new managed object: {ex.Message}");
        }

        return default;
    }
    
    [UnmanagedCallersOnly]
    public static unsafe IntPtr LookupManagedMethod(IntPtr typeHandlePtr, char* methodName)
    {
        try
        {
            string methodNameString = new string(methodName);
            RuntimeTypeHandle typeHandle = RuntimeTypeHandle.FromIntPtr(typeHandlePtr);
            Type? foundType = Type.GetTypeFromHandle(typeHandle);

            if (typeHandle == null || foundType == null)
            {
                throw new Exception("Couldn't find type with TypeHandle");
            }
            
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            var currentType = foundType;
            
            while (currentType != null)
            {
                MethodInfo? method = currentType.GetMethod(methodNameString, flags);

                if (method != null)
                {
                    return method.MethodHandle.GetFunctionPointer();
                }
                
                currentType = currentType.BaseType;
            }

            return default;
        }
        catch (Exception e)
        {
            LogUnrealSharp.LogError($"Exception while trying to look up managed method: {e.Message}");
        }

        return default;
    }

    [UnmanagedCallersOnly]
    public static unsafe IntPtr LookupManagedType(IntPtr assemblyHandle, char* typeNamespace, char* typeEngineName)
    {
        try
        {
            string typeNamespaceString = new string(typeNamespace);
            string typeEngineNameString = new string(typeEngineName);
            string fullTypeName = $"{typeNamespaceString}.{typeEngineNameString}";
            
            if (assemblyHandle == IntPtr.Zero)
            {
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                
                foreach (Assembly assembly in assemblies)
                {
                    IntPtr foundType = FindTypeInAssembly(assembly, fullTypeName);
                    if (foundType != IntPtr.Zero)
                    {
                        return foundType;
                    }
                }
                throw new Exception($"The type '{fullTypeName}' was not found in any loaded assemblies.");
            }

            // Load the specific assembly if assemblyHandle is provided
            Assembly? loadedAssembly = GCHandle.FromIntPtr(assemblyHandle).Target as Assembly;

            if (loadedAssembly == null)
            {
                throw new InvalidOperationException("The provided assembly handle does not point to a valid assembly.");
            }

            return FindTypeInAssembly(loadedAssembly, fullTypeName);
        }
        catch (TypeLoadException ex)
        {
            LogUnrealSharp.LogError($"TypeLoadException while trying to look up managed type: {ex.Message}");
            return default;
        }
    }
    
    private static IntPtr FindTypeInAssembly(Assembly assembly, string fullTypeName)
    {
        Type[] types = assembly.GetTypes();
        foreach (Type type in types)
        {
            foreach (CustomAttributeData attributeData in type.CustomAttributes)
            {
                if (attributeData.AttributeType.FullName != typeof(GeneratedTypeAttribute).FullName)
                {
                    continue;
                }

                if (attributeData.ConstructorArguments.Count != 2)
                {
                    continue;
                }

                string fullName = (string)attributeData.ConstructorArguments[1].Value!;
                if (fullName == fullTypeName)
                {
                    return type.TypeHandle.Value;
                }
            }
        }

        return IntPtr.Zero;
    }
    
    [UnmanagedCallersOnly]
    public static unsafe int InvokeManagedMethod(IntPtr managedObjectHandle,
        delegate*<object, IntPtr, IntPtr, void> methodPtr, 
        IntPtr argumentsBuffer, 
        IntPtr returnValueBuffer, 
        IntPtr exceptionTextBuffer)
    {
        try
        {
            object? managedObject = GCHandle.FromIntPtr(managedObjectHandle).Target;
            
            if (managedObject == null)
            {
                throw new ArgumentNullException(nameof(managedObject));
            }
            
            methodPtr(managedObject, argumentsBuffer, returnValueBuffer);
            return 0;
        }
        catch (Exception ex)
        {
            StringMarshaller.ToNative(exceptionTextBuffer, 0, ex.ToString());
            LogUnrealSharp.LogError($"Exception during InvokeManagedMethod: {ex.Message}");
            return 1;
        }
    }

    [UnmanagedCallersOnly]
    public static void InvokeDelegate(IntPtr delegatePtr)
    {
        try
        {
            if (delegatePtr == IntPtr.Zero)
            {
                return;
            }

            GCHandle foundHandle = GCHandle.FromIntPtr(delegatePtr);

            if (foundHandle.Target is not Delegate @delegate)
            {
                return;
            }

            @delegate.DynamicInvoke();
        }
        catch (Exception ex)
        {
            LogUnrealSharp.LogError($"Exception during InvokeDelegate: {ex.Message}");
        }
    }

    [UnmanagedCallersOnly]
    public static void Dispose(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        GCHandle foundHandle = GCHandle.FromIntPtr(handle);

        if (foundHandle.Target is IDisposable disposable)
        {
            disposable.Dispose();
        }

        GcHandleUtilities.Free(foundHandle);
    }
}