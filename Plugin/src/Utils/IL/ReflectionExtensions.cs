using System;
using System.Reflection;

namespace EntranceTeleportOptimizations.Utils.IL;

public static class ReflectionExtensions
{
    public static MethodInfo GetGenericMethod(this Type type, string name, Type[] parameters, Type[] genericArgs)
    {
        var methods = type.GetMethods();
        foreach (var method in methods)
        {
            if (method.Name != name)
                continue;
            if (!method.IsGenericMethodDefinition)
                continue;

            var candidateParameters = method.GetParameters();
            if (parameters.Length != candidateParameters.Length)
                continue;
            var parametersEqual = true;
            for (var i = 0; i < parameters.Length; i++)
            {
                if (parameters[i] != candidateParameters[i].ParameterType)
                {
                    parametersEqual = false;
                    break;
                }
            }

            if (!parametersEqual)
                continue;

            return method.MakeGenericMethod(genericArgs);
        }

        return null;
    }
}