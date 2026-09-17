using System;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.TestTools;

namespace DuckRoulette.Tests
{
    // Game scripts compile into Assembly-CSharp with no asmdef and keep most of their logic private,
    // so the tests reach it through reflection instead of widening production visibility.
    internal static class Reflect
    {
        private const BindingFlags AnyMember = BindingFlags.Instance | BindingFlags.Static |
                                               BindingFlags.Public | BindingFlags.NonPublic |
                                               BindingFlags.DeclaredOnly;

        public static object Call(object target, string methodName, params object[] args)
        {
            args ??= Array.Empty<object>();
            (Type type, object instance) = Split(target);

            for (Type t = type; t != null; t = t.BaseType)
            {
                MethodInfo[] candidates = t.GetMethods(AnyMember)
                    .Where(m => m.Name == methodName && m.GetParameters().Length == args.Length)
                    .ToArray();

                if (candidates.Length == 1)
                {
                    return Invoke(candidates[0], instance, args);
                }

                if (candidates.Length > 1)
                {
                    throw new AmbiguousMatchException($"{type.Name}.{methodName} has {candidates.Length} overloads with {args.Length} parameters - use CallTyped.");
                }
            }

            throw new MissingMethodException(type.FullName, methodName);
        }

        public static object CallTyped(object target, string methodName, Type[] parameterTypes, params object[] args)
        {
            (Type type, object instance) = Split(target);

            for (Type t = type; t != null; t = t.BaseType)
            {
                MethodInfo method = t.GetMethod(methodName, AnyMember, null, parameterTypes, null);
                if (method != null)
                {
                    return Invoke(method, instance, args);
                }
            }

            throw new MissingMethodException(type.FullName, methodName);
        }

        public static T Get<T>(object target, string memberName)
        {
            (Type type, object instance) = Split(target);

            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo field = t.GetField(memberName, AnyMember) ?? t.GetField($"<{memberName}>k__BackingField", AnyMember);
                if (field != null)
                {
                    return (T)field.GetValue(instance);
                }

                PropertyInfo property = t.GetProperty(memberName, AnyMember);
                if (property != null)
                {
                    return (T)property.GetValue(instance);
                }
            }

            throw new MissingMemberException(type.FullName, memberName);
        }

        public static void Set(object target, string memberName, object value)
        {
            (Type type, object instance) = Split(target);

            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo field = t.GetField(memberName, AnyMember) ?? t.GetField($"<{memberName}>k__BackingField", AnyMember);
                if (field != null)
                {
                    field.SetValue(instance, value);
                    return;
                }

                PropertyInfo property = t.GetProperty(memberName, AnyMember);
                MethodInfo setter = property?.GetSetMethod(true);
                if (setter != null)
                {
                    setter.Invoke(instance, new[] { value });
                    return;
                }
            }

            throw new MissingMemberException(type.FullName, memberName);
        }

        // Pass a System.Type as the target to address static members.
        private static (Type, object) Split(object target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            return target is Type staticType ? (staticType, null) : (target.GetType(), target);
        }

        private static object Invoke(MethodInfo method, object instance, object[] args)
        {
            try
            {
                return method.Invoke(instance, args);
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }
    }

    internal static class OfflineNetcode
    {
        // Netcode's IL post-processor injects this error + early return into every RPC body when no
        // NetworkManager is listening. Offline unit tests hit it whenever server logic broadcasts.
        private static readonly Regex RpcNotListening =
            new Regex(Regex.Escape("Rpc methods can only be invoked after starting the NetworkManager!"));

        public static void ExpectRpcCalls(int count)
        {
            for (int i = 0; i < count; i++)
            {
                LogAssert.Expect(LogType.Error, RpcNotListening);
            }
        }
    }
}
