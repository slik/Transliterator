using System.Text;

namespace Translit;

public readonly record struct Emit(int Backspaces, string Text, bool PassThrough);

public sealed class Transliterator
{
    private char? _last;
    private readonly Stack<char?> _history = new();

    public Emit ProcessChar(char input)
    {
        _history.Push(_last);
        if (_last is {} last) {
            var comboKey = string.Concat(char.ToLowerInvariant(last), char.ToLowerInvariant(input));
            if (TranslitTable.Map.TryGetValue(comboKey, out var cv)) {
                var outStr = char.IsUpper(last) ? cv.ToUpperInvariant() : cv;
                if (TranslitTable.ResetAfter.Contains(outStr[^1])) {
                    _last = null;
                } else {
                    _last = outStr[^1];
                }

                return new Emit(1, outStr, false);
            }
        }

        var baseKey = char.ToLowerInvariant(input).ToString();
        if (TranslitTable.Map.TryGetValue(baseKey, out var bv)) {
            var outStr = char.IsUpper(input) ? bv.ToUpperInvariant() : bv;
            if (TranslitTable.ResetAfter.Contains(outStr[^1])) {
                _last = null;
            } else {
                _last = outStr[^1];
            }

            return new Emit(0, outStr, false);
        }

        _last = null;

        return new Emit(0, "", true);
    }

    public void Undo() => _last = _history.Count > 0 ? _history.Pop() : null;

    public void Reset()
    {
        _last = null;
        _history.Clear();
    }

    public static string TransliterateString(string input)
    {
        var t = new Transliterator();
        var sb = new StringBuilder();
        foreach (var c in input) {
            var e = t.ProcessChar(c);
            if (e.PassThrough) {
                sb.Append(c);
            } else {
                if (e.Backspaces > 0)
                    sb.Length = Math.Max(0, sb.Length - e.Backspaces);
                sb.Append(e.Text);
            }
        }

        return sb.ToString();
    }
}