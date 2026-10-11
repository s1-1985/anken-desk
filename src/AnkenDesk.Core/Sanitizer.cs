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

        /// <summary>Windowsのパスの長さの上限（260）に、余裕をもたせた値。</summary>
        public const int MaxPathLength = 250;

        /// <summary>連番（" (99)"）を付けても収まるように空けておく長さ。</summary>
        private const int SuffixReserve = 6;

        /// <summary>
        /// フォルダ <paramref name="dir"/> に <paramref name="fileName"/> で保存したときのパスが、Windowsの上限に収まるように、
        /// 名前（拡張子の前）を短く切る。長いメールの件名や添付のファイル名で、保存に失敗しないようにするため。
        /// 収まるときはそのまま返す。拡張子は残す。フォルダ自体が長すぎて収まらないときも、名前を可能な限り短くして返す（最低8文字）。
        /// </summary>
        public static string FitFileName(string dir, string fileName)
        {
            var allowed = MaxPathLength - (dir ?? "").TrimEnd('\\', '/').Length - 1 - SuffixReserve;
            if (fileName.Length <= allowed)
            {
                return fileName;
            }

            var dot = fileName.LastIndexOf('.');
            var ext = dot > 0 && fileName.Length - dot <= 10 ? fileName.Substring(dot) : "";
            var stem = ext.Length > 0 ? fileName.Substring(0, dot) : fileName;
            var keep = Math.Max(8, allowed - ext.Length);
            if (keep >= stem.Length)
            {
                return fileName;
            }

            // 絵文字などのサロゲートペアの途中で切らない。
            if (keep > 0 && char.IsHighSurrogate(stem[keep - 1]))
            {
                keep--;
            }

            return stem.Substring(0, keep).TrimEnd('.', ' ', '　') + ext;
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
