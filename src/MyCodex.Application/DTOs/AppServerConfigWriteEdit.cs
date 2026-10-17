namespace MyCodex.Application.DTOs
{
    public sealed class AppServerConfigWriteEdit
    {
        public string KeyPath { get; set; }

        public string MergeStrategy { get; set; }

        public object Value { get; set; }

        public static AppServerConfigWriteEdit CreateReplace(string keyPath, object value)
        {
            return new AppServerConfigWriteEdit
            {
                KeyPath = keyPath,
                MergeStrategy = "replace",
                Value = value
            };
        }
    }
}
