using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MacroKit.Tests
{
    /// <summary>Marks a public static parameterless method as a test.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TestAttribute : Attribute { }

    public sealed class AssertionException : Exception
    {
        public AssertionException(string message) : base(message) { }
    }

    /// <summary>Assertions with messages that show both values.</summary>
    public static class Assert
    {
        public static void Fail(string message) { throw new AssertionException(message); }

        public static void True(bool condition, string message = null)
        {
            if (!condition) throw new AssertionException(message ?? "Expected true but was false.");
        }

        public static void False(bool condition, string message = null)
        {
            if (condition) throw new AssertionException(message ?? "Expected false but was true.");
        }

        public static void Equal<T>(T expected, T actual, string message = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new AssertionException((message == null ? "" : message + ": ") + "expected " + Show(expected) + " but was " + Show(actual) + ".");
        }

        public static void NotNull(object value, string message = null)
        {
            if (value == null) throw new AssertionException(message ?? "Expected a value but was null.");
        }

        public static void Null(object value, string message = null)
        {
            if (value != null) throw new AssertionException((message == null ? "" : message + ": ") + "expected null but was " + Show(value) + ".");
        }

        public static void Contains(string text, string part, string message = null)
        {
            if (text == null || text.IndexOf(part, StringComparison.Ordinal) < 0)
                throw new AssertionException((message == null ? "" : message + ": ") + "expected " + Show(text) + " to contain " + Show(part) + ".");
        }

        /// <summary>Runs the action and returns the exception of type T it must throw (an exact type or a subclass).</summary>
        public static T Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T expected) { return expected; }
            catch (Exception other) { throw new AssertionException("Expected " + typeof(T).Name + " but got " + other.GetType().Name + ": " + other.Message); }
            throw new AssertionException("Expected " + typeof(T).Name + " but nothing was thrown.");
        }

        /// <summary>Same items in the same order; the message names the first difference.</summary>
        public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message = null)
        {
            List<T> e = expected.ToList(), a = actual.ToList();
            string prefix = message == null ? "" : message + ": ";
            for (int i = 0; i < Math.Min(e.Count, a.Count); i++)
            {
                if (!EqualityComparer<T>.Default.Equals(e[i], a[i]))
                    throw new AssertionException(prefix + "item " + i + " differs: expected " + Show(e[i]) + " but was " + Show(a[i]) + ".\n  expected: " + Show(e) + "\n  actual:   " + Show(a));
            }
            if (e.Count != a.Count)
                throw new AssertionException(prefix + "expected " + e.Count + " items but got " + a.Count + ".\n  expected: " + Show(e) + "\n  actual:   " + Show(a));
        }

        public static string Show(object value)
        {
            if (value == null) return "null";
            var text = value as string;
            if (text != null) return "\"" + text.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") + "\"";
            var items = value as IEnumerable;
            if (items != null)
            {
                var parts = new List<string>();
                foreach (object item in items) parts.Add(Show(item));
                return "[" + string.Join(", ", parts) + "]";
            }
            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>A temporary folder that is deleted when disposed.</summary>
    public sealed class TempDir : IDisposable
    {
        public string Path { get; private set; }

        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MacroKit-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string File(string name) { return System.IO.Path.Combine(Path, name); }

        public void Dispose()
        {
            try { Directory.Delete(Path, true); } catch { }
        }
    }

    /// <summary>Discovers and runs every [Test] method. Optional argument: only tests whose name contains it.</summary>
    public static class TestRunner
    {
        public static int Main(string[] args)
        {
            string filter = args.Length > 0 ? args[0] : null;
            var tests = new List<MethodInfo>();
            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static).OrderBy(m => m.Name, StringComparer.Ordinal))
                {
                    if (method.GetCustomAttribute<TestAttribute>() == null || method.GetParameters().Length != 0) continue;
                    string name = type.Name + "." + method.Name;
                    if (filter == null || name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) tests.Add(method);
                }
            }

            int passed = 0, failed = 0;
            foreach (MethodInfo method in tests)
            {
                string name = method.DeclaringType.Name + "." + method.Name;
                try
                {
                    method.Invoke(null, null);
                    passed++;
                    Console.WriteLine("PASS " + name);
                }
                catch (Exception e)
                {
                    failed++;
                    Exception inner = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                    string detail = inner is AssertionException ? inner.Message : inner.GetType().Name + ": " + inner.Message + "\n" + inner.StackTrace;
                    Console.WriteLine("FAIL " + name + ": " + detail);
                }
            }
            Console.WriteLine(passed + " passed, " + failed + " failed");
            return failed;
        }
    }
}
