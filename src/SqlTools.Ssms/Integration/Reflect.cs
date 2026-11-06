using System;
using System.Linq;
using System.Reflection;

namespace SqlTools.Ssms.Integration
{
    /// <summary>Acesso a membros internos do SSMS. Procura na hierarquia inteira; nunca lança.</summary>
    internal static class Reflect
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        public static object Get(object target, string name)
        {
            if (target == null) return null;
            try
            {
                for (var t = target.GetType(); t != null; t = t.BaseType)
                {
                    var p = t.GetProperty(name, All);
                    if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(target, null);
                    var f = t.GetField(name, All);
                    if (f != null) return f.GetValue(target);
                }
            }
            catch
            {
                // membro mudou ou lançou: tratar como ausente
            }
            return null;
        }

        public static T Get<T>(object target, string name) where T : class => Get(target, name) as T;

        public static object GetStatic(Type type, string name)
        {
            if (type == null) return null;
            try
            {
                for (var t = type; t != null; t = t.BaseType)
                {
                    var p = t.GetProperty(name, All);
                    if (p != null) return p.GetValue(null, null);
                    var f = t.GetField(name, All);
                    if (f != null) return f.GetValue(null);
                }
            }
            catch
            {
            }
            return null;
        }

        public static EventInfo FindEvent(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var e = t.GetEvent(name, All);
                if (e != null) return e;
            }
            return null;
        }

        public static Type FindType(string fullName, string assemblyNameHint)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name.Equals(assemblyNameHint, StringComparison.OrdinalIgnoreCase)))
            {
                var t = a.GetType(fullName, false);
                if (t != null) return t;
            }
            try
            {
                var asm = Assembly.Load(assemblyNameHint);
                return asm.GetType(fullName, false);
            }
            catch
            {
                return null;
            }
        }

        public static object Invoke(object target, string method, params object[] args)
        {
            if (target == null) return null;
            for (var t = target.GetType(); t != null; t = t.BaseType)
            {
                var m = t.GetMethods(All).FirstOrDefault(x => x.Name == method && x.GetParameters().Length == args.Length);
                if (m != null) return m.Invoke(target, args);
            }
            throw new MissingMethodException(target.GetType().FullName, method);
        }
    }
}
