using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace Kehai.Karen
{
    // `-karen-set field=value` (0.4.0, build plan C1.2): any public KarenConfig field for one test
    // arm, from the command line, without a new build. Numbers, switches, words and enums:
    //   -karen-set huntMemorySeconds=12 -karen-set shield=off -karen-set brain=vital
    // A field it doesn't know, or a value it can't read, is refused with the reason, and nothing
    // is changed: a typo in an arm must not quietly run the default.
    public static class KarenConfigSet
    {
        public static bool Apply(KarenConfig config, string assignment, out string why)
        {
            why = null;
            if (config == null || string.IsNullOrEmpty(assignment)) { why = "nothing to set"; return false; }
            int eq = assignment.IndexOf('=');
            if (eq <= 0) { why = $"'{assignment}' is not field=value"; return false; }
            string name = assignment.Substring(0, eq).Trim();
            string text = assignment.Substring(eq + 1).Trim();

            FieldInfo field = typeof(KarenConfig).GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (field == null) { why = $"KarenConfig has no field '{name}'"; return false; }

            if (!TryRead(field.FieldType, text, out object value)) { why = $"'{text}' isn't a {field.FieldType.Name} for {field.Name}"; return false; }
            field.SetValue(config, value);
            return true;
        }

        // Every assignment in turn; the ones refused are returned with their reasons.
        public static List<string> ApplyAll(KarenConfig config, IEnumerable<string> assignments)
        {
            var refused = new List<string>();
            foreach (string a in assignments)
                if (!Apply(config, a, out string why)) refused.Add(why);
            return refused;
        }

        static bool TryRead(System.Type type, string text, out object value)
        {
            value = null;
            if (type == typeof(string)) { value = text; return true; }
            if (type == typeof(bool))
            {
                string t = text.ToLowerInvariant();
                if (t == "on" || t == "true" || t == "1" || t == "yes") { value = true; return true; }
                if (t == "off" || t == "false" || t == "0" || t == "no") { value = false; return true; }
                return false;
            }
            if (type == typeof(float))
            {
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) return false;
                value = f;
                return true;
            }
            if (type == typeof(int))
            {
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) return false;
                value = i;
                return true;
            }
            if (type.IsEnum)
            {
                foreach (string n in System.Enum.GetNames(type))
                    if (string.Equals(n, text, System.StringComparison.OrdinalIgnoreCase)
                        || n.StartsWith(text + "_", System.StringComparison.OrdinalIgnoreCase))
                    {
                        value = System.Enum.Parse(type, n);
                        return true;
                    }
                return false;
            }
            return false;
        }
    }
}
