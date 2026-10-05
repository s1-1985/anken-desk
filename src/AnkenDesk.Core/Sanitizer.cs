using System;
using System.Text;

namespace AnkenDesk.Core
{
    /// <summary>Windowsのフォルダ名・ファイル名に使えない文字を置き換える。</summary>
    public static class Sanitizer
    {
        // Linuxでも同じ結果になるよう、Path.GetInvalidFileNameChars() ではなく固定の一覧を使う。
        private static readonly char[] InvalidChars = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };

        private static readonly string[] ReservedNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        /// <summary>使えない文字を「_」に置き換え、前後の空白と末尾のピリオドを取り、予約名を避ける。</summary>
        public static string FileName(string? name)
        {
            var sb = new StringBuilder();
            foreach (var c in (name ?? "").Trim())
            {
                sb.Append(IsInvalid(c) ? '_' : c);
            }

            var s = sb.ToString().TrimEnd('.', ' ', '　');
            if (IsReserved(s))
            {
                s = "_" + s;
            }

            return s;
        }

        /// <summary>置き換えが要らない名前か（使えない文字・予約名・空でない）。</summary>
        public static bool IsValidName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            var trimmed = name!.Trim();
            return FileName(trimmed) == trimmed;
        }

        private static bool IsInvalid(char c)
        {
            return c < ' ' || Array.IndexOf(InvalidChars, c) >= 0;
        }

        private static bool IsReserved(string s)
        {
            var dot = s.IndexOf('.');
            var stem = dot >= 0 ? s.Substring(0, dot) : s;
            return Array.IndexOf(ReservedNames, stem.ToUpperInvariant()) >= 0;
        }
    }
}
