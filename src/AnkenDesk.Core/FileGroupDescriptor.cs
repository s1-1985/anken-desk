using System;
using System.Collections.Generic;
using System.Text;

namespace AnkenDesk.Core
{
    /// <summary>
    /// ドラッグ&ドロップの FileGroupDescriptorW（仮想ファイルの一覧）から、ファイル名を取り出す。
    /// 並び: 先頭に件数（4バイト）、続けて FILEDESCRIPTORW を件数だけ（1つ592バイト。ファイル名は72バイト目からの WCHAR[260]）。
    /// OutlookのメールのドラッグはメールをFileGroupDescriptorWの「件名.msg」として渡す。
    /// </summary>
    public static class FileGroupDescriptor
    {
        private const int HeaderSize = 4;
        private const int ItemSize = 592;
        private const int NameOffset = 72;
        private const int NameBytes = 520;

        public static IReadOnlyList<string> FileNames(byte[] bytes)
        {
            var names = new List<string>();
            if (bytes == null || bytes.Length < HeaderSize + ItemSize)
            {
                return names;
            }

            var count = BitConverter.ToInt32(bytes, 0);
            for (var i = 0; i < count && i < 1000 && HeaderSize + (i + 1) * ItemSize <= bytes.Length; i++)
            {
                var name = Encoding.Unicode.GetString(bytes, HeaderSize + i * ItemSize + NameOffset, NameBytes);
                var end = name.IndexOf('\0');
                names.Add(end >= 0 ? name.Substring(0, end) : name);
            }

            return names;
        }

        public static bool HasMsg(byte[] bytes)
        {
            foreach (var n in FileNames(bytes))
            {
                if (n.EndsWith(".msg", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
