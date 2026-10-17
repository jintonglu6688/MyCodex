using System;

namespace MyCodex.Application.RemoteRelay
{
    public static class SecureRemoteBase64Url
    {
        public static string Encode(byte[] value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return Convert.ToBase64String(value)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        public static byte[] Decode(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if ((value.Length % 4) == 1)
            {
                throw new FormatException("The Base64Url value has an invalid length.");
            }

            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                var valid =
                    (character >= 'A' && character <= 'Z') ||
                    (character >= 'a' && character <= 'z') ||
                    (character >= '0' && character <= '9') ||
                    character == '-' ||
                    character == '_';
                if (!valid)
                {
                    throw new FormatException("The Base64Url value contains an invalid character.");
                }
            }

            var padded = value
                .Replace('-', '+')
                .Replace('_', '/')
                .PadRight(value.Length + ((4 - (value.Length % 4)) % 4), '=');
            var decoded = Convert.FromBase64String(padded);
            if (!string.Equals(Encode(decoded), value, StringComparison.Ordinal))
            {
                Array.Clear(decoded, 0, decoded.Length);
                throw new FormatException("The Base64Url value is not canonical.");
            }

            return decoded;
        }
    }
}
