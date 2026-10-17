using System;
using System.Text;

namespace MyCodex.Application.CodingRemote
{
    public static class RemoteConversationUtf8Slice
    {
        public const int PreviewByteLimit = 64 * 1024;
        public const int ContentChunkByteLimit = 256 * 1024;

        public static string Take(string text, int byteLimit, out bool truncated, out long totalBytes)
        {
            text = Normalize(text);
            totalBytes = new UTF8Encoding(false, true).GetByteCount(text);
            if (byteLimit <= 0 || totalBytes <= byteLimit)
            {
                truncated = false;
                return text;
            }

            var result = new StringBuilder();
            var bytes = 0;
            for (var index = 0; index < text.Length;)
            {
                var length = char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]) ? 2 : 1;
                var candidate = text.Substring(index, length);
                var candidateBytes = new UTF8Encoding(false, true).GetByteCount(candidate);
                if (bytes + candidateBytes > byteLimit)
                {
                    break;
                }

                result.Append(candidate);
                bytes += candidateBytes;
                index += length;
            }

            truncated = true;
            return result.ToString();
        }

        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text ?? string.Empty;
            }

            var result = new StringBuilder(text.Length);
            for (var index = 0; index < text.Length; index++)
            {
                var character = text[index];
                if (char.IsHighSurrogate(character))
                {
                    if (index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
                    {
                        result.Append(character).Append(text[++index]);
                    }
                    else
                    {
                        result.Append('\uFFFD');
                    }
                }
                else if (char.IsLowSurrogate(character))
                {
                    result.Append('\uFFFD');
                }
                else
                {
                    result.Append(character);
                }
            }

            return result.ToString();
        }

        public static string Slice(string text, long utf8Offset, int byteLimit, out long bytesRead, out long totalBytes)
        {
            text = Normalize(text);
            totalBytes = new UTF8Encoding(false, true).GetByteCount(text);
            utf8Offset = Math.Max(0L, utf8Offset);
            var result = new StringBuilder();
            long position = 0;
            bytesRead = 0;
            for (var index = 0; index < text.Length;)
            {
                var length = char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]) ? 2 : 1;
                var candidate = text.Substring(index, length);
                var candidateBytes = new UTF8Encoding(false, true).GetByteCount(candidate);
                if (position + candidateBytes <= utf8Offset)
                {
                    position += candidateBytes;
                    index += length;
                    continue;
                }

                if (position < utf8Offset || bytesRead + candidateBytes > byteLimit)
                {
                    break;
                }

                result.Append(candidate);
                bytesRead += candidateBytes;
                position += candidateBytes;
                index += length;
            }

            return result.ToString();
        }
    }
}
