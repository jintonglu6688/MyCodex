namespace MyCodex.Shared.Helpers
{
    public static class TextValueHelper
    {
        /// <summary>Returns the trimmed value, or null when the value is null, empty, or white space.</summary>
        public static string TrimToNull(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>Returns the first value that is not null or white space, trimmed; otherwise an empty string.</summary>
        public static string FirstNonEmptyTrimmed(params string[] values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            for (var i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    return values[i].Trim();
                }
            }

            return string.Empty;
        }
    }
}
