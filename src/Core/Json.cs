using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace MacroKit.Core
{
    /// <summary>
    /// JSON for Macro Kit's files. Parsing uses JavaScriptSerializer (MaxJsonLength = int.MaxValue); writing and the mapping
    /// onto model classes are done here so that enums are written as their names, files are pretty-printed, recording events are
    /// compact [t,kind,a,b] arrays, and reading is tolerant: unknown properties are ignored, missing ones keep the class defaults,
    /// and a property of the wrong type or an unknown enum name keeps its default instead of failing the whole file.
    /// Models are classes with public get/set properties and a public parameterless constructor.
    /// </summary>
    public static class Json
    {
        static readonly Dictionary<Type, PropertyInfo[]> propertyCache = new Dictionary<Type, PropertyInfo[]>();

        // ---- writing ----------------------------------------------------------------------------

        /// <summary>Pretty-printed JSON (two-space indent) ending with a line break.</summary>
        public static string Serialize(object value)
        {
            var text = new StringBuilder();
            WriteValue(text, value, 0);
            text.Append('\n');
            return text.ToString();
        }

        static void WriteValue(StringBuilder text, object value, int depth)
        {
            if (value == null) { text.Append("null"); return; }
            Type type = value.GetType();
            if (value is string) { WriteString(text, (string)value); return; }
            if (value is bool) { text.Append((bool)value ? "true" : "false"); return; }
            if (type.IsEnum) { WriteString(text, value.ToString()); return; }
            if (value is RecEvent) { WriteEvent(text, (RecEvent)value); return; }
            if (value is double || value is float)
            {
                double d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                // JSON has no NaN or infinity: such a value reads back as "missing", which keeps the class default.
                text.Append(double.IsNaN(d) || double.IsInfinity(d) ? "null" : d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (type.IsPrimitive || value is decimal) { text.Append(Convert.ToString(value, CultureInfo.InvariantCulture)); return; }
            var list = value as IEnumerable;
            if (list != null) { WriteArray(text, list, depth); return; }
            WriteObject(text, value, type, depth);
        }

        static void WriteArray(StringBuilder text, IEnumerable list, int depth)
        {
            var items = new List<object>();
            foreach (object item in list) items.Add(item);
            if (items.Count == 0) { text.Append("[]"); return; }
            bool inline = true;
            foreach (object item in items)
            {
                if (item != null && !(item is string) && !item.GetType().IsPrimitive && !item.GetType().IsEnum && !(item is decimal)) { inline = false; break; }
            }
            if (inline)
            {
                text.Append('[');
                for (int i = 0; i < items.Count; i++)
                {
                    if (i > 0) text.Append(", ");
                    WriteValue(text, items[i], depth);
                }
                text.Append(']');
                return;
            }
            text.Append("[\n");
            for (int i = 0; i < items.Count; i++)
            {
                text.Append(' ', (depth + 1) * 2);
                WriteValue(text, items[i], depth + 1);
                text.Append(i < items.Count - 1 ? ",\n" : "\n");
            }
            text.Append(' ', depth * 2).Append(']');
        }

        static void WriteObject(StringBuilder text, object value, Type type, int depth)
        {
            PropertyInfo[] properties = GetProperties(type);
            if (properties.Length == 0) { text.Append("{}"); return; }
            text.Append("{\n");
            for (int i = 0; i < properties.Length; i++)
            {
                text.Append(' ', (depth + 1) * 2);
                WriteString(text, properties[i].Name);
                text.Append(": ");
                WriteValue(text, properties[i].GetValue(value, null), depth + 1);
                text.Append(i < properties.Length - 1 ? ",\n" : "\n");
            }
            text.Append(' ', depth * 2).Append('}');
        }

        static void WriteEvent(StringBuilder text, RecEvent e)
        {
            text.Append('[').Append(e.T.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(((int)e.Kind).ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(e.A.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(e.B.ToString(CultureInfo.InvariantCulture)).Append(']');
        }

        static void WriteString(StringBuilder text, string s)
        {
            text.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    case '\b': text.Append("\\b"); break;
                    case '\f': text.Append("\\f"); break;
                    default:
                        if (c < 0x20) text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else text.Append(c);
                        break;
                }
            }
            text.Append('"');
        }

        // Public read/write instance properties, in declaration order. Read-only members (IsEmpty and the like) are computed, not data.
        static PropertyInfo[] GetProperties(Type type)
        {
            lock (propertyCache)
            {
                PropertyInfo[] found;
                if (propertyCache.TryGetValue(type, out found)) return found;
                var list = new List<PropertyInfo>();
                foreach (PropertyInfo p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0 && !Attribute.IsDefined(p, typeof(ScriptIgnoreAttribute))) list.Add(p);
                }
                found = list.ToArray();
                propertyCache[type] = found;
                return found;
            }
        }

        // ---- reading ----------------------------------------------------------------------------

        /// <summary>Parses JSON text into dictionaries, arrays and scalars. Throws ArgumentException on a syntax error.</summary>
        public static object ParseTree(string json)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 200 };
            return serializer.DeserializeObject(json);
        }

        /// <summary>Parses JSON text into a T. Throws when the text is not valid JSON or its top level is not what T needs.</summary>
        public static T Deserialize<T>(string json)
        {
            object tree = ParseTree(json);
            if (tree == null) throw new ArgumentException("The JSON text is empty or null.");
            return FromTree<T>(tree);
        }

        public static T FromTree<T>(object tree)
        {
            return (T)ConvertNode(typeof(T), tree);
        }

        /// <summary>True when the parsed tree is an object that has the named property (case-insensitive).</summary>
        public static bool HasProperty(object tree, string name)
        {
            var dictionary = tree as IDictionary<string, object>;
            if (dictionary == null) return false;
            foreach (string key in dictionary.Keys) if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static object ConvertNode(Type type, object node)
        {
            Type nullable = Nullable.GetUnderlyingType(type);
            if (nullable != null) type = nullable;
            if (node == null) return type.IsValueType ? Activator.CreateInstance(type) : null;

            if (type == typeof(string))
            {
                if (node is string) return node;
                if (node is IDictionary || node is IEnumerable) throw new FormatException("Expected text.");
                return Convert.ToString(node, CultureInfo.InvariantCulture);
            }
            if (type == typeof(bool))
            {
                if (node is bool) return node;
                bool parsed;
                if (node is string && bool.TryParse((string)node, out parsed)) return parsed;
                throw new FormatException("Expected true or false.");
            }
            if (type.IsEnum)
            {
                var name = node as string;
                if (name != null)
                {
                    object value;
                    try { value = Enum.Parse(type, name, true); } catch (ArgumentException) { throw new FormatException("Unknown name " + name); }
                    if (!Enum.IsDefined(type, value)) throw new FormatException("Unknown name " + name);
                    return value;
                }
                object number = Enum.ToObject(type, Convert.ToInt64(node, CultureInfo.InvariantCulture));
                if (!Enum.IsDefined(type, number)) throw new FormatException("Unknown value.");
                return number;
            }
            if (type == typeof(double) || type == typeof(float) || type == typeof(decimal) || type.IsPrimitive)
            {
                if (node is bool || node is IEnumerable) throw new FormatException("Expected a number.");
                if (type == typeof(double)) return Convert.ToDouble(node, CultureInfo.InvariantCulture);
                if (type == typeof(float)) return Convert.ToSingle(node, CultureInfo.InvariantCulture);
                if (type == typeof(decimal)) return Convert.ToDecimal(node, CultureInfo.InvariantCulture);
                double number = Convert.ToDouble(node, CultureInfo.InvariantCulture);
                return Convert.ChangeType(Math.Round(number), type, CultureInfo.InvariantCulture);   // throws OverflowException when out of range
            }
            if (type == typeof(RecEvent)) return ConvertEvent(node);
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return ConvertList(type, node);
            if (type.IsClass) return ConvertObject(type, node);
            throw new FormatException("Unsupported type " + type.Name);
        }

        static object ConvertEvent(object node)
        {
            var parts = node as IList;
            if (parts == null || parts.Count < 4) throw new FormatException("A recording event is [time, kind, a, b].");
            int kind = Convert.ToInt32(parts[1], CultureInfo.InvariantCulture);
            if (!Enum.IsDefined(typeof(RecKind), kind)) throw new FormatException("Unknown event kind.");
            return new RecEvent(
                Convert.ToInt32(parts[0], CultureInfo.InvariantCulture), (RecKind)kind,
                Convert.ToInt32(parts[2], CultureInfo.InvariantCulture), Convert.ToInt32(parts[3], CultureInfo.InvariantCulture));
        }

        static object ConvertList(Type listType, object node)
        {
            var source = node as IEnumerable;
            if (source == null || node is string || node is IDictionary) throw new FormatException("Expected a list.");
            Type elementType = listType.GetGenericArguments()[0];
            var result = (IList)Activator.CreateInstance(listType);
            foreach (object item in source)
            {
                try { result.Add(ConvertNode(elementType, item)); }
                catch (Exception e) when (e is FormatException || e is InvalidCastException || e is OverflowException) { /* skip an entry that does not fit */ }
            }
            return result;
        }

        static object ConvertObject(Type type, object node)
        {
            var dictionary = node as IDictionary<string, object>;
            if (dictionary == null) throw new FormatException("Expected an object.");
            object result = Activator.CreateInstance(type);
            var byName = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (PropertyInfo p in GetProperties(type)) byName[p.Name] = p;
            foreach (KeyValuePair<string, object> entry in dictionary)
            {
                PropertyInfo property;
                if (entry.Value == null || !byName.TryGetValue(entry.Key, out property)) continue;   // unknown or null: keep the default
                try { property.SetValue(result, ConvertNode(property.PropertyType, entry.Value), null); }
                catch (Exception e) when (e is FormatException || e is InvalidCastException || e is OverflowException) { /* wrong type: keep the default */ }
            }
            return result;
        }
    }
}
